using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace SimpleJSON
{
    public partial class Parser
    {
        static MethodInfo addIEnumerable = typeof(Parser).GetMethod("AddIEnumerable",BindingFlags.NonPublic|BindingFlags.Instance);
        static MethodInfo addIDictionary = typeof(Parser).GetMethod("AddDictionary", BindingFlags.NonPublic|BindingFlags.Instance);
        public static string ToJson<T>(T obj)
        {
            if (obj == null)
            {
                return "null";
            }
            Parser p = new Parser();
            StringBuilder text = new StringBuilder();
            p.json<T>(obj, text);
            return text.ToString();
        }

        private void json<T>(T obj, StringBuilder text)
        {
            if (obj == null)
            {
                text.Append("null");
                return;
            }
            text.Append("{");
            Type targetType = typeof(T);
            lock (classPropertyCacheLock)
            {
                if (!classPropertyCache.ContainsKey(targetType))
                {
                    classPropertyCache[targetType] = new Dictionary<string, PropertyInfo>();
                    foreach (PropertyInfo item in targetType.GetProperties())
                    {
                        classPropertyCache[targetType].Add(item.Name, item);
                    }
                }
            }
            int propertiesCount = 0;
            foreach (PropertyInfo item in classPropertyCache[targetType].Values)
            {
                if (!ICollectionArgumentCache.ContainsKey(item.PropertyType))
                {
                    lock (ICollectionArgumentCacheLock)
                    {
                        if (!ICollectionArgumentCache.ContainsKey(item.PropertyType))
                            ICollectionArgumentCache[item.PropertyType] = item.PropertyType.GetInterface("IEnumerable`1");
                    }
                }
                Type hasGenericIEnumerable = ICollectionArgumentCache[item.PropertyType];
                text.Append("\"");
                text.Append(item.Name);
                text.Append("\":");
                object o = null;
                o = item.GetValue(obj, null);
                switch (item.PropertyType.Name)
                {
                    case "Boolean":
                        if (o.ToString().ToLower()=="true")
                        {
                            text.Append("true");
                        }
                        else
                        {
                            text.Append("false");
                        }
                        break;
                    case "Byte":
                    case "Int16":
                    case "UInt16":
                    case "Int32":
                    case "UInt32":
                    case "Int64":
                    case "UInt64":
                    case "Decimal":
                    case "Double":
                    case "String":
                    case "DateTime":
                    case "TimeSpan":
                        if (o==null)
                        {
                            text.Append("null");
                        }
                        else
                        {
                            text.Append("\"");
                            text.Append(o.ToString());
                            text.Append("\"");
                        }
                        break;
                    default:
                        if (o==null)
                        {
                            text.Append("null");
                        }
                        else
                        {
                            if (hasGenericIEnumerable != null)
                            {
                                addIEnumerable.MakeGenericMethod(hasGenericIEnumerable.GetGenericArguments()).
                                Invoke(this, new object[] { o, text });
                            }
                            else
                            {
                                text.Append("\"");
                                text.Append(o.ToString());
                                text.Append("\"");
                            }
                        }
                        
                        break;
                }
                text.Append(",");
                ++propertiesCount;
            }
            if (propertiesCount > 0)
            {
                text.Remove(text.Length - 1, 1);
            }
            text.Append("}");
        }

        private void AddIEnumerable<K>(IEnumerable<K> inst, StringBuilder text)
        {
            if (inst == null)
            {
                text.Append("null");
                return;
            }
            Type kType = typeof(K);
            text.Append("[");
            int i = 0;
            if (kType.IsGenericType)
            {
                
                Type baseType = kType.GetInterface("IDictionary`2");
                foreach (K item in inst)
                {
                    if (baseType != null)
                    {
                        addIDictionary.MakeGenericMethod(kType.GetGenericArguments()).Invoke(this, new object[] { item, text });
                    }
                    text.Append(",");
                    ++i;
                }
            }
            else
            {
                if (inst != null)
                {
                    foreach (K item in inst)
                    {
                        switch (kType.Name)
                        {
                            case "Boolean":
                                if (item.ToString().ToLower() == "true")
                                {
                                    text.Append("true");
                                }
                                else
                                {
                                    text.Append("false");
                                }
                                break;
                            case "Byte":
                            case "Int16":
                            case "UInt16":
                            case "Int32":
                            case "UInt32":
                            case "Int64":
                            case "UInt64":
                            case "Decimal":
                            case "Double":
                            case "String":
                            case "DateTime":
                            case "TimeSpan":
                                if (item == null)
                                {
                                    text.Append("null");
                                }
                                else
                                {
                                    text.Append("\"");
                                    text.Append(item.ToString());
                                    text.Append("\"");
                                }
                                break;
                            default:
                                if (!ICollectionArgumentCache.ContainsKey(kType))
                                {
                                    lock (ICollectionArgumentCacheLock)
                                    {
                                        if (!ICollectionArgumentCache.ContainsKey(kType))
                                            ICollectionArgumentCache[kType] = kType.GetInterface("IEnumerable`1");
                                    }
                                }
                                Type hasGenericIEnumerable = ICollectionArgumentCache[kType];
                                if (hasGenericIEnumerable != null)
                                {
                                    addIEnumerable.MakeGenericMethod(hasGenericIEnumerable.GetGenericArguments()).Invoke(this, new object[] { item, text });
                                }
                                else
                                {
                                    json<K>(item, text);
                                }
                                break;
                        }
                        text.Append(",");
                        ++i;
                    }
                }
            }
            if (i > 0)
            {
                text.Remove(text.Length - 1, 1);
            }
            text.Append("]");

        }

        private void AddDictionary<K, V>(Dictionary<K, V> inst, StringBuilder text)
        {
            if (inst == null)
            {
                text.Append("null");
                return;
            }
            text.Append("[");
            int i = 0;
            Type vType = typeof(V);
            foreach (KeyValuePair<K, V> item in inst)
            {
                text.Append("\"");
                text.Append(item.Key.ToString());
                text.Append("\":");
                switch (vType.Name)
                {
                    case "Boolean":
                        if (item.Value.ToString().ToLower()=="true")
                        {
                            text.Append("true");
                        }
                        else
                        {
                            text.Append("false");
                        }
                        break;
                    case "Byte":
                    case "Int16":
                    case "UInt16":
                    case "Int32":
                    case "UInt32":
                    case "Int64":
                    case "UInt64":
                    case "Decimal":
                    case "Double":
                    case "String":
                    case "DateTime":
                    case "TimeSpan":
                        if (item.Value == null)
                        {
                            text.Append("null");
                        }
                        else
                        {
                            text.Append("\"");
                            text.Append(item.Value.ToString());
                            text.Append("\"");
                        }
                        break;
                    default:
                        if (!ICollectionArgumentCache.ContainsKey(vType))
                        {
                            lock (ICollectionArgumentCacheLock)
                            {
                                if (!ICollectionArgumentCache.ContainsKey(vType))
                                    ICollectionArgumentCache[vType] = vType.GetInterface("IEnumerable`1");
                            }
                        }
                        Type hasGenericIEnumerable = ICollectionArgumentCache[vType];
                        if (hasGenericIEnumerable != null)
                        {
                            addIEnumerable.MakeGenericMethod(hasGenericIEnumerable.GetGenericArguments()).Invoke(this, new object[] { item.Value, text });
                        }
                        else
                        {
                            json<V>(item.Value, text);
                        }
                        break;
                }
                text.Append(",");
                ++i;
            }
            if (i > 0)
            {
                text.Remove(text.Length - 1, 1);
            }
            text.Append("]");
        }
    }
}