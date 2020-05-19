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

        private List<Signal> SignalsDeserialized = new List<Signal>();
        public void ConfigureSerialization()
        {
            BsonMapperCustom mapper = new BsonMapperCustom();
            //build db path
            var dbPath = Path.Combine(MyDataDir, "MyData.db");
            if (!Directory.Exists(MyDataDir))
                Directory.CreateDirectory(MyDataDir);

            //init db 
            this.Db = new LiteDatabase(dbPath, mapper);
            var closedOperationsCollection = this.Db.GetCollection<Operation>("ClosedOperations");
            var activeOperationsCollection = this.Db.GetCollection<Operation>("ActiveOperations");
            closedOperationsCollection.EnsureIndex(oper => oper.CreationTime);
            activeOperationsCollection.EnsureIndex(oper => oper.Id);

            this.Db.Pragma("UTC_DATE", true);
             
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
        }
    }
}
