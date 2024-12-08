using LiteDB;
using SharpTrader.Core.BrokersApi.Binance;

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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
        private BsonMapperCustom DbMapper;
        private ILiteCollection<Operation> DbClosedOperations;
        private ILiteCollection<Operation> DbActiveOperations;

        private Dictionary<string, Signal> SignalsDeserialized = new Dictionary<string, Signal>();
        private BsonMapperCustom NaiveMapper = new BsonMapperCustom();
        private BsonMapperCustom defaultMapper;
        private EntityMapper signalMapper;

        public void ConfigureSerialization()
        {
            defaultMapper = new BsonMapperCustom();

            NaiveMapper.RegisterType<ISymbolInfo>(
                serialize: obj => defaultMapper.Serialize<ISymbolInfo>(obj),
                deserialize: bson =>
                {
                    if (bson["_type"] != BsonValue.Null)
                        return defaultMapper.Deserialize<ISymbolInfo>(bson);
                    return defaultMapper.Deserialize<BinanceSymbolInfo>(bson);
                });
            NaiveMapper.RegisterType<IOrder>(
                serialize: null,
                deserialize: bson =>
                {
                    if (bson["_type"] != BsonValue.Null)
                        return defaultMapper.Deserialize<IOrder>(bson);
                    return defaultMapper.Deserialize<SharpTrader.MarketSimulator.Order>(bson);
                }
            );
            NaiveMapper.RegisterType<ITrade>(
               serialize: null,
               deserialize: bson =>
               {
                   if (bson["_type"] != BsonValue.Null)
                       return defaultMapper.Deserialize<ITrade>(bson);
                   return defaultMapper.Deserialize<SharpTrader.MarketSimulator.Trade>(bson);
               }
            );
            NaiveMapper.RegisterType<Signal>(
               serialize: o => defaultMapper.Serialize<Signal>(o),
               deserialize: bson =>
               {
                   var id = bson["Id"].AsString ?? bson["_id"].AsString;
                   var signal = new Signal(id);
                   NaiveMapper.PopulateObjectProperties(signalMapper, signal, bson as BsonDocument);
                   return signal;
               }
            );
            DbMapper = new BsonMapperCustom();
            signalMapper = DbMapper.BuildEntityMapper(typeof(Signal));
            Market.RegisterCustomSerializers(DbMapper);
            this.Executor?.RegisterCustomSerializers(DbMapper);
            this.RiskManager?.RegisterCustomSerializers(DbMapper);
            this.Sentry?.RegisterCustomSerializers(DbMapper);

            //---- add mapper for signals  

            DbMapper.RegisterType<Signal>(
                serialize: (obj) => defaultMapper.Serialize<Signal>(obj),
                deserialize: (bson) =>
                {
                    var id = bson["Id"].AsString ?? bson["_id"].AsString;
                    //we must assure that there is only one instance of the same signal

                    if (!SignalsDeserialized.TryGetValue(id, out var signal))
                    {
                        signal = new Signal(id);
                        SignalsDeserialized.Add(id, signal);
                        DbMapper.PopulateObjectProperties(signalMapper, signal, bson as BsonDocument);
                    }
                    return signal;

                }
            );


            //build db path
            var dbPath = Path.Combine(MyDataDir, "MyData.db");
            if (!Directory.Exists(MyDataDir))
                Directory.CreateDirectory(MyDataDir);

            //init db 
            this.Db = new LiteDatabase(dbPath, DbMapper);
            this.Db.Pragma("UTC_DATE", true);
            DbClosedOperations = this.Db.GetCollection<Operation>("ClosedOperations");
            DbClosedOperations.EnsureIndex(oper => oper.CreationTime);
            DbActiveOperations = this.Db.GetCollection<Operation>("ActiveOperations");
            DbActiveOperations.EnsureIndex(oper => oper.Id);
            Db.Checkpoint();

            //purge old operations that never got to active state
            Logger.Information("Purging old operations and rebuilding database.");
            var purgeLimit = this.Time - TimeSpan.FromDays(2);
            List<string> operationsToRemove = new List<string>();

            foreach (var oper in this.Db.GetCollection("ClosedOperations").FindAll())
            {
                var op = this.OperationFromBson(oper);

                var isOld = op.CreationTime < purgeLimit;
                var entryZero = op.AmountInvested <= 0 && op.AmountLiquidated <= 0;
                if (isOld && entryZero)
                    operationsToRemove.Add(op.Id);
            }


            Db.BeginTrans();
            foreach (var opId in operationsToRemove)
            {
                DbClosedOperations.Delete(opId);
            }

            Db.Commit();
            Db.Checkpoint();
            Db.Rebuild();
            Logger.Information("Rebuild completed.");
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
            if (!Config.SaveData)
                return;
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
                {
                    DbActiveOperations.Upsert(op);
                    op.AcceptChanges();
                }

                foreach (var op in ClosedOperations.Where(op => op.IsChanged))
                {
                    DbClosedOperations.Upsert(op);
                    op.AcceptChanges();
                }
                // remove closed operations that have been there for more than 1 hour
                ClearClosedOperations(TimeSpan.FromHours(1));

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
                        op.AcceptChanges();
                    }

                    foreach (var op in DbActiveOperations.FindAll().ToArray())
                    {
                        op.Recalculate();
                        DbActiveOperations.Upsert(op);
                        op.AcceptChanges();
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
        public Operation[] QueryClosedOperations(Expression<Func<BsonDocument, bool>> predicate)
        {
            lock (DbLock)
            {
                var l1 = this.Db.GetCollection("ClosedOperations").Find(predicate).ToList();
                return l1.Select(bson => OperationFromBson(bson)).ToArray();
            }
        }
        public Operation QueryClosedOperationsById(string id)
        {
            lock (DbLock)
            {
                var doclist = this.Db.GetCollection("ClosedOperations").FindOne(id);
                return OperationFromBson(doclist);
            }
        }
        public Operation[] QueryOperations(Expression<Func<Operation, bool>> predicate)
        {
            lock (DbLock)
            {
                var l1 = this.DbClosedOperations.Find(predicate).ToList();
                var l2 = this.DbActiveOperations.Find(predicate).ToList();
                return l1.Concat(l2).ToArray();
            }
        }

        /// <summary>
        /// Deserialize an operation from bson document without keeping references ( all of the referenced objects are clones )
        /// for performance reasons
        /// </summary> 
        public Operation OperationFromBson(BsonDocument operBson)
        {
            return NaiveMapper.Deserialize<Operation>(operBson);
        }
    }
}
