using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace SharpTrader
{
    

    public class ParamsSet
    {
        public ParamsSet(string prop, object[] pars)
        {
            this.Property = prop;
            this.Values = pars;
        }

        public string Property { get; set; }
        public object[] Values { get; set; }
    }



    public class OptimizationSpace
    {
        private List<ParamsSet> ParamsSets = new List<ParamsSet>();
        private List<object> Configs = new List<object>();
        private Type ConfigType;

        public IReadOnlyList<object> Configurations => Configs;

        public OptimizationSpace(Type configType)
        {
            ConfigType = configType;
        }

        public void Optimize(string property, params object[] values)
        {
            ParamsSets.Add(new ParamsSet(property, values.Cast<object>().ToArray()));
        }

        public void Initialize()
        {
            List<int[]> permutations = new List<int[]>();
            int[] currentPerm = new int[ParamsSets.Count];
            bool Increment(int i)
            {
                if (i == currentPerm.Length)
                    return false; //we created all possible permutations

                currentPerm[i] += 1;
                if (currentPerm[i] == ParamsSets[i].Values.Length)
                {
                    currentPerm[i] = 0;
                    return Increment(i + 1);
                }
                else
                    return true;
            }
            permutations.Add(currentPerm.ToArray());
            while (Increment(0))
                permutations.Add(currentPerm.ToArray());

            foreach (var permutation in permutations)
            {
                var config = Activator.CreateInstance(ConfigType);
                for (int i = 0; i < ParamsSets.Count; i++)
                {
                    var property = ConfigType.GetProperty(ParamsSets[i].Property);
                    var val = ConvertType(ParamsSets[i].Values[permutation[i]], property.PropertyType) ;

                    property.SetValue(config, val);
                }
                Configs.Add(config);
            }
        }

        private static IEnumerable<PropertyInfo> GetPublicProperties(  Type type)
        {
            if (!type.IsInterface)
                return type.GetProperties();

            return (new Type[] { type })
                   .Concat(type.GetInterfaces())
                   .SelectMany(i => i.GetProperties());
        }

        private object ConvertType(object obj, Type target) {
            if (target == typeof(TimeSpan))
                return TimeSpan.Parse(obj as string);
            else
                return Convert.ChangeType(obj, target);
        }

        public string ToJson()
        {
            SerializationInfo info = new SerializationInfo()
            {
                ConfigType = this.ConfigType.AssemblyQualifiedName,
                ParamsSets = this.ParamsSets
            };
            JsonSerializerSettings settings = new JsonSerializerSettings()
            { 
             

            };
            return  JsonConvert.SerializeObject(info, settings);
        }

        public static OptimizationSpace FromJson(string json)
        { 
            SerializationInfo info = Newtonsoft.Json.JsonConvert.DeserializeObject<SerializationInfo>(json);
            var type = Type.GetType(info.ConfigType, true);
            var obj = new OptimizationSpace(type);
            obj.ParamsSets = info.ParamsSets;
            obj.Initialize();
            return obj;
        }

        public class SerializationInfo
        {
            public List<ParamsSet> ParamsSets { get; set; }
            public string ConfigType { get; set; }
        }
    }
}
