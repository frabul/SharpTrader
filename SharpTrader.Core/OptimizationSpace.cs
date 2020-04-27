using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace SharpTrader
{
    public class OptimizationSpace
    {
        private List<(string prop, object[] pars)> Values = new List<(string, object[])>();
        private List<int> OptimizationIndexes = new List<int>();
        private int Cursor = -1;
        public IEnumerable<(string prop, object val)> ParamsSet
        {
            get
            {
                for (int i = 0; i < Values.Count; i++)
                {
                    yield return (Values[i].prop, Values[i].pars[OptimizationIndexes[i]]);
                }
            }
        }

        public T Optimize<T>(string property, params T[] values)
        {
            Cursor++;
            if (Values.Count <= Cursor)
            {
                Values.Add((property, values.Cast<object>().ToArray()));
                OptimizationIndexes.Add(0);
            }
            else
            {
                //check that values are equal
                Debug.Assert(property.Equals(Values[Cursor].prop));
                Debug.Assert(values.Length == Values[Cursor].pars.Length);
                for (int i = 0; i < values.Length; i++)
                {
                    Debug.Assert(values[i].Equals(Values[Cursor].pars[i]));
                }

            }
            return (T)Values[Cursor].pars[OptimizationIndexes[Cursor]];
        }

        public IEnumerable<OptimizationSpace> GetPermutations()
        {
            List<int[]> permutations = new List<int[]>();
            int[] currentPerm = new int[Values.Count];


            bool Increment(int i)
            {
                if (i == currentPerm.Length)
                    return false; //we created all possible permutations

                currentPerm[i] += 1;
                if (currentPerm[i] == Values[i].pars.Length)
                {
                    currentPerm[i] = 0;
                    return Increment(i + 1);
                }
                else
                    return true;
            }
            permutations.Add(currentPerm.ToArray());
            while (Increment(0))
            {
                permutations.Add(currentPerm.ToArray());
            }
            var spaces = permutations
                .Select(p => new OptimizationSpace()
                {
                    Values = this.Values,
                    OptimizationIndexes = p.ToList(),
                });
            return spaces;
        }


    }

    public class ParamsSet
    {
        public ParamsSet(string prop, object[] pars)
        {
            this.prop = prop;
            this.pars = pars;
        }

        public string prop { get; set; }
        public object[] pars { get; set; }
    }



    public class OptimizationSpace2
    {
        private List<ParamsSet> ParamsSets = new List<ParamsSet>();
        private List<object> Configs = new List<object>();
        private Type ConfigType;

        public IReadOnlyList<object> Configurations => Configs;
        public OptimizationSpace2(Type configType)
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
                if (currentPerm[i] == ParamsSets[i].pars.Length)
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
                    var property = ConfigType.GetProperty(ParamsSets[i].prop);
                    var val = ConvertType(ParamsSets[i].pars[permutation[i]], property.PropertyType) ;

                    property.SetValue(config, val);
                }
                Configs.Add(config);
            }
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
                TypeNameHandling = TypeNameHandling.All,
                Formatting = Formatting.Indented,

            };
            return  JsonConvert.SerializeObject(info, settings);
        }

        public static OptimizationSpace2 FromJson(string json)
        { 
            SerializationInfo info = Newtonsoft.Json.JsonConvert.DeserializeObject<SerializationInfo>(json);
            var type = Type.GetType(info.ConfigType, true);
            var obj = new OptimizationSpace2(type);
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
