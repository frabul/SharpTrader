using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;

namespace SharpTrader.AlgoFramework
{
    public class BsonMapperCustom : BsonMapper
    {
        new public EntityMapper BuildEntityMapper(Type type) => base.BuildEntityMapper(type);

        public void PopulateObjectProperties(EntityMapper entity, object obj, BsonDocument value)
        {
            foreach (var member in entity.Members.Where(x => x.Setter != null))
            {
                if (value.TryGetValue(member.FieldName, out var val))
                {
                    // check if has a custom deserialize function
                    if (member.Deserialize != null)
                    {
                        member.Setter(obj, member.Deserialize(val, this));
                    }
                    else
                    {
                        member.Setter(obj, this.Deserialize(member.DataType, val));
                    }
                }
            }
        }

        /// <summary>
        /// Serializes an object using a specific entity mapper
        /// </summary> 
        public BsonDocument SerializeObject<T>(EntityMapper entity, object obj)
        {
            var t = obj.GetType();
            var doc = new BsonDocument();


            // adding _type only where property Type is not same as object instance type
            if (typeof(T) != t)
            {
                doc["_type"] = new BsonValue(DefaultTypeNameBinder.Instance.GetName(t));
            }

            foreach (var member in entity.Members.Where(x => x.Getter != null))
            {
                // get member value
                var value = member.Getter(obj);

                if (value == null && this.SerializeNullValues == false && member.FieldName != "_id") continue;

                // if member has a custom serialization, use it
                if (member.Serialize != null)
                {
                    doc[member.FieldName] = member.Serialize(value, this);
                }
                else
                {
                    doc[member.FieldName] = this.Serialize(member.DataType, value);
                }
            }

            return doc;
        }

    }
    public abstract partial class TradingAlgo : TraderBot
    {
        private object DbLock = new object();
        private LiteDatabase Db;
        private ILiteCollection<Operation> DbClosedOperations;
        private ILiteCollection<Operation> DbActiveOperations;

        private List<Signal> SignalsDeserialized = new List<Signal>();
        public void ConfigureSerialization()
        {
            BsonMapperCustom mapper = new BsonMapperCustom(); 

            Market.RegisterSerializationHandlers(mapper);
            this.Executor?.RegisterSerializationHandlers(mapper);
            this.RiskManager?.RegisterSerializationHandlers(mapper);
            this.Sentry?.RegisterSerializationHandlers(mapper);

            //---- add mapper for SymbolInfo
            mapper.Entity<SymbolInfo>().Ctor(
                bson =>
                    {
                        var sym = Market.GetSymbols().FirstOrDefault(s => s.Key == bson["Key"].AsString);
                        if (sym == null)
                        {
                            sym = new SymbolInfo();
                            var entityMapper = mapper.BuildEntityMapper(typeof(SymbolInfo));
                            mapper.PopulateObjectProperties(entityMapper, sym, bson);
                        }
                        return sym;
                    }

                    );
            //---- add mapper for signals
            Signal deserializeSignal(BsonValue bson)
            {
                var id = bson["Id"].AsString ?? bson["_id"].AsString;
                //we must assure that there is only one instance of the same signal
                var signal = SignalsDeserialized.FirstOrDefault(s => s.Id == id);
                if (signal == null)
                {
                    signal = new Signal(id);
                    SignalsDeserialized.Add(signal);
                }
                return signal;
            }
            mapper.Entity<Signal>().Ctor(deserializeSignal);


            //build db path
            var dbPath = Path.Combine(MyDataDir, "MyData.db");
            if (!Directory.Exists(MyDataDir))
                Directory.CreateDirectory(MyDataDir);

            //init db 
            this.Db = new LiteDatabase(dbPath, mapper);
            this.Db.Pragma("UTC_DATE", true);
            DbClosedOperations = this.Db.GetCollection<Operation>("ClosedOperations");
            DbClosedOperations.EnsureIndex(oper => oper.CreationTime);
            DbActiveOperations = this.Db.GetCollection<Operation>("ActiveOperations");
            DbActiveOperations.EnsureIndex(oper => oper.Id);

            //purge old operations that never got to active state
            Logger.Info("Purgin old operations and rebuilding database.");
            var purgeLimit = this.Time - TimeSpan.FromDays(7);
            List<string> operationsToRemove = new List<string>();
            foreach (var oper in this.Db.GetCollection("ClosedOperations").FindAll())
            {
                var isOld = oper["CreationTime"].AsDateTime < purgeLimit;
                var entryZero = oper["AmountInvested"].AsDecimal <= 0 && oper["AmountLiquidated"].AsDecimal <= 0;
                if (isOld && entryZero)
                    operationsToRemove.Add(oper["_id"].AsString);
            }
            Db.BeginTrans();
            foreach (var opId in operationsToRemove)
            {
                DbClosedOperations.Delete(opId);
            }

            Db.Commit();
            Db.Checkpoint();
            Db.Rebuild();
            Logger.Info("Rebuld completed.");
        }
        /// <summary>
        /// This function should provide and object that is going to be saved for reload after reset
        /// </summary> 
        protected virtual object GetState() { return new object(); }
        /// <summary>
        /// This function receives the state saved ( provided by GetState() ) and restore the internal variables
        /// </summary> 
        protected virtual void RestoreState(object state) { }

        public void SaveNonVolatileVars()
        {
            lock (DbLock)
            {
                //save my internal state
                BsonDocument state = new BsonDocument();
                state["_id"] = "TradingAlgoState";
                state["State"] = Db.Mapper.Serialize(State);

                //Save derived state  
                state["DerivedClassState"] = Db.Mapper.Serialize(GetState());

                //save module states 
                state["Sentry"] = Db.Mapper.Serialize(Sentry.GetState());
                state["Allocator"] = Db.Mapper.Serialize(Allocator.GetState());
                state["Executor"] = Db.Mapper.Serialize(Executor.GetState());
                state["RiskManager"] = Db.Mapper.Serialize(RiskManager.GetState());

                Db.GetCollection("State").Upsert(state);

                foreach (var symData in SymbolsData.Values)
                    Db.GetCollection<SymbolData>("SymbolsData").Upsert(symData);

                Db.BeginTrans();
                foreach (var op in ActiveOperations.Where(op => op.IsChanged))
                    DbActiveOperations.Upsert(op);
                foreach (var op in ClosedOperations.Where(op => op.IsChanged))
                    DbClosedOperations.Upsert(op);
                Db.Commit();

                Db.Checkpoint();
            }
        }

        public void LoadNonVolatileVars()
        {
            lock (DbLock)
            {
                //reload my internal state
                BsonDocument states = Db.GetCollection("State").FindById("TradingAlgoState");
                if (states != null)
                {
                    State = Db.Mapper.Deserialize<NonVolatileVars>(states["State"]);

                    //reload derived class state
                    this.RestoreState(Db.Mapper.Deserialize<object>(states["DerivedClassState"]));

                    //reload modules state
                    Sentry.RestoreState(Db.Mapper.Deserialize<object>(states["Sentry"]));
                    Allocator.RestoreState(Db.Mapper.Deserialize<object>(states["Allocator"]));
                    Executor.RestoreState(Db.Mapper.Deserialize<object>(states["Executor"]));
                    RiskManager.RestoreState(Db.Mapper.Deserialize<object>(states["RiskManager"]));
                }



                //rebuild symbols data
                foreach (var symData in Db.GetCollection<SymbolData>("SymbolsData").FindAll())
                    _SymbolsData[symData.Id] = symData;

                if (Db.UserVersion < 4)
                {

                    foreach (var op in DbClosedOperations.FindAll().ToArray())
                    {
                        op.Recalculate();
                        DbClosedOperations.Upsert(op);
                    }

                    foreach (var op in DbActiveOperations.FindAll().ToArray())
                    {
                        op.Recalculate();
                        DbActiveOperations.Upsert(op);
                    }

                    Db.UserVersion = 4;
                    Db.Checkpoint();
                    Db.Rebuild();
                }

                //rebuild operations 
                //closed operations are not loaded in current session   
                foreach (var op in DbActiveOperations.FindAll().ToArray())
                    this.AddActiveOperation(op);
            }
        }

        public BsonDocument[] QueryOperations(Expression<Func<BsonDocument, bool>> predicate)
        {
            lock (DbLock)
            {
                var l1 = this.Db.GetCollection("ClosedOperations").Find(predicate).ToList();
                var l2 = this.Db.GetCollection("ActiveOperations").Find(predicate).ToList();
				return l1.Concat(l2).ToArray();
            }
        }
    }
}
