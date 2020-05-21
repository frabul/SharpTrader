using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public abstract partial class TradingAlgo
    {
        private ILiteCollection<Operation> DbClosedOperations;
        private ILiteCollection<Operation> DbActiveOperations;

        private List<Signal> SignalsDeserialized = new List<Signal>();
        public void ConfigureSerialization()
        {
            BsonMapperCustom mapper = new BsonMapperCustom(); 

            //todo register mapper for custom components
            Market.RegisterSerializationHandlers(mapper);
            this.Executor?.RegisterSerializationHandlers(mapper);
            this.RiskManager?.RegisterSerializationHandlers(mapper);
            this.Sentry?.RegisterSerializationHandlers(mapper);

            //---- add mapper for SymbolInfo
            mapper.Entity<SymbolInfo>().Ctor(
                bson => Market.GetSymbols().FirstOrDefault(s => s.Key == bson["Key"].AsString));
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
            //save my internal state
            BsonDocument states = new BsonDocument();
            states["_id"] = "TradingAlgoState";
            states["State"] = Db.Mapper.Serialize(State);

            //Save derived state  
            states["DerivedClassState"] = Db.Mapper.Serialize(GetState());

            //save module states 
            states["Sentry"] = Db.Mapper.Serialize(Sentry.GetState());
            states["Allocator"] = Db.Mapper.Serialize(Allocator.GetState());
            states["Executor"] = Db.Mapper.Serialize(Executor.GetState());
            states["RiskManager"] = Db.Mapper.Serialize(RiskManager.GetState());

            Db.GetCollection("State").Upsert(states);

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

        public void LoadNonVolatileVars()
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

            if (Db.UserVersion == 0)
            {
              
                foreach (var op in DbClosedOperations.FindAll().ToArray())
                    DbClosedOperations.Upsert(op);

             
                foreach (var op in DbClosedOperations.FindAll().ToArray())
                    DbClosedOperations.Upsert(op); 
                Db.UserVersion = 1;
                Db.Checkpoint();
                Db.Rebuild();
            }

            //rebuild operations 
            //closed operations are not loaded in current session   
            foreach (var op in DbActiveOperations.FindAll().ToArray()) 
                this.AddActiveOperation(op); 
        }
    }
}
