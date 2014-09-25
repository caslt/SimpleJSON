
using System;
using System.Collections.Generic;
using System.Reflection;

namespace SimpleJSON
{
    public partial class Parser
    {
        private readonly static Dictionary<Type, Dictionary<string, PropertyInfo>> classPropertyCache = new Dictionary<Type, Dictionary<string, PropertyInfo>>();
        private readonly static object classPropertyCacheLock = new object();
        private readonly static Dictionary<Type, Type> ICollectionArgumentCache = new Dictionary<Type, Type>();
        private readonly static object ICollectionArgumentCacheLock = new object();
        private readonly static Dictionary<Type, KeyValuePair<Type, Type>> IDictionaryArgumentCache = new Dictionary<Type, KeyValuePair<Type, Type>>();
        private readonly static object IDictionaryArgumentCacheLock = new object();
        private readonly static Dictionary<Type, Dictionary<string, MethodInfo>> methodCache = new Dictionary<Type, Dictionary<string, MethodInfo>>();
        private readonly static object methodCacheLock = new object();
        private readonly static Dictionary<Type, Type> IEnumerableArgumentCache = new Dictionary<Type, Type>();
        private readonly static object IEnumerableCacheLock = new object();

        public Stack<Type> ObjectTypeStack = new Stack<Type>();
        public Stack<Object> ObjectCursor = new Stack<object>();
        internal Cursor LastToken = null;


        public uint Column = 1;
        public uint Line = 1;
        public int Index = 0;
        /// <summary>
        /// {"Name":"Alias","ID":1}
        /// When Index is 5 ("), Currentkey = "Name"
        /// When Index is 13 ("), Currentkey = null
        /// When Index is 18 ("), Currentkey = "ID"
        /// </summary>
        public string CurrentKey = null;
        /// <summary>
        /// {"Name":"Alias"}
        /// When Index is between A and s, IsInStringSegment = true
        /// </summary>
        public bool IsInStringSegment = false;
        /// <summary>
        /// Example
        /// {"Name":"Alias"}
        /// When Index is between A and s, IsValue = true
        /// </summary>
        public bool IsValue = false;
        /// <summary>
        /// {"ID":3}
        /// For numberic processing
        /// </summary>
        public bool IsDoubleProcessing = false;// Processing double, in case we meet \t\r\n during processing double
        public string JSONStr = null;
        public object rootObj = null;

        public Type RootType = null;
        private static Type ICollection = typeof(ICollection<>);
        private static Type IDictionaryGeneric = typeof(IDictionary<,>);
        private static string ErrorMSG = "Invalid JSON data! At line {0}, column {1}";
        private static string PropertyNotFoundMSG = "Property {0} not found for Type {1}!";
        private static string UnsupportErrorMSG = "Property {0} in type {1} is an UnSupported structure.";

        public static T Parse<T>(string str)
        {
            if (str == null)
            {
                throw new ArgumentNullException(str);
            }
            Parser parser = new Parser();
            parser.JSONStr = str;
            T root = Activator.CreateInstance<T>();
            parser.rootObj = root;
            Type t = typeof(T);
            parser.RootType = t;
            parser.ObjectTypeStack.Push(t);
            parser.ObjectCursor.Push(root);

            lock (classPropertyCacheLock)
            {
                if (!classPropertyCache.ContainsKey(t))
                {
                    classPropertyCache[t] = new Dictionary<string, PropertyInfo>();
                    foreach (PropertyInfo item in t.GetProperties())
                    {
                        classPropertyCache[t].Add(item.Name, item);
                    }
                }
            }
            if (!parser.CheckIDictionaryInterface(t))
            {
                parser.CheckICollectionInterface(t);
            }

            while (parser.Index < str.Length)
            {
                switch (str[parser.Index])
                {
                    case '{':
                        if (parser.IsInStringSegment)
                        {
                            ++parser.Column;
                            ++parser.Index;
                            break;
                        }
                        parser.ProcessLCB();
                        break;
                    case '}':
                        if (parser.IsInStringSegment)
                        {
                            ++parser.Column;
                            ++parser.Index;
                            break;
                        }
                        parser.ProcessRCB();
                        break;
                    case '\"':
                        parser.ProcessQuote();
                        break;
                    case ',':
                        if (parser.IsInStringSegment)
                        {
                            ++parser.Column;
                            ++parser.Index;
                            break;
                        }
                        parser.ProcessComma();
                        break;
                    case '[':
                        if (parser.IsInStringSegment)
                        {
                            ++parser.Column;
                            ++parser.Index;
                            break;
                        }
                        parser.ProcessLSB();
                        break;
                    case ']':
                        if (parser.IsInStringSegment)
                        {
                            ++parser.Column;
                            ++parser.Index;
                            break;
                        }
                        parser.ProcessRSB();
                        break;
                    case ':':
                        if (parser.IsInStringSegment)
                        {
                            ++parser.Column;
                            ++parser.Index;
                            break;
                        }
                        parser.ProcessColon();
                        break;
                    /* true or false */
                    case 't':
                    case 'T':
                    case 'f':
                    case 'F':
                        if (parser.IsInStringSegment)
                        {
                            ++parser.Column;
                            ++parser.Index;
                            break;
                        }
                        parser.ProcessBollean();
                        break;
                    /* null */
                    case 'n':
                    case 'N':
                        if (parser.IsInStringSegment)
                        {
                            ++parser.Column;
                            ++parser.Index;
                            break;
                        }
                        parser.ProcessNULL();
                        break;
                    case ' ':
                    case '\r':
                    case '\t':
                    case '\f':
                    case '\b':
                        ++parser.Index;
                        ++parser.Column;
                        break;
                    case '\n':
                        if (parser.IsInStringSegment)
                        {
                            ++parser.Index;
                            ++parser.Column;
                        }
                        else
                        {
                            ++parser.Line;
                            parser.Column = 1;
                            ++parser.Index;
                        }
                        break;
                    default:
                        if (parser.IsInStringSegment || parser.IsDoubleProcessing)
                        {
                            ++parser.Column;
                            ++parser.Index;
                            break;
                        }
                        parser.ProcessDefault();
                        break;
                }
            }
            if (parser.ObjectTypeStack.Count > 0)
            {
                throw new Exception("Invalid JSON data!");
            }
            return root;
        }

        void ProcessLCB()
        {
            //First element
            if (LastToken == null)
            {
                if (RootType.IsArray)
                {
                    CallException();
                }
                else
                {
                    if (LastToken == null)
                    {
                        LastToken = new Cursor();
                    }
                    LastToken.Position = Index;
                    LastToken.Token = '{';
                    ++Index;
                    ++Column;
                    return;
                }
            }
            if (IsDoubleProcessing)
            {
                CallException();
            }
            if (LastToken.Token != ':'  /*This is a object, "string":{}*/   && LastToken.Token != ','/*This is an Array,[{},{},{}]*/  && LastToken.Token != '[')
            {
                CallException();
            }
            if (LastToken.Token == ':' && CurrentKey == null)//If json object is placed after colon, then there must be a key for this array
            {
                CallException();
            }
            Type targetType = null;
            Type parentType = ObjectTypeStack.Peek();
            object parent = ObjectCursor.Peek();
            if (LastToken.Token == '[' || (LastToken.Token == ','))
            {
                targetType = ICollectionArgumentCache[parentType];
            }
            else
            {
                if (!classPropertyCache[parentType].ContainsKey(CurrentKey))
                {
                    CallPropertyNotFoundException(parentType);
                }
                targetType = classPropertyCache[parentType][CurrentKey].PropertyType;
            }

            object obj = Activator.CreateInstance(targetType);

            



            //If it's parent implemented IList<>
            if (CheckICollectionInterface(parentType))
            {
                MethodInfo addMethod = null;

                lock (methodCacheLock)
                {
                    if (!methodCache.ContainsKey(parentType))
                    {
                        methodCache[parentType] = new Dictionary<string, MethodInfo>();
                        addMethod = parentType.GetMethod("Add");
                        methodCache[parentType]["Add"] = addMethod;
                    }
                    else
                    {
                        addMethod = methodCache[parentType]["Add"];
                    }
                }

                addMethod.Invoke(parent, new object[] { obj });
                CurrentKey = null;
                ObjectTypeStack.Push(targetType);
                ObjectCursor.Push(obj);
            }
            else //If not
            {
                classPropertyCache[parentType][CurrentKey].SetValue(parent, obj, null);
                ObjectTypeStack.Push(targetType);
                ObjectCursor.Push(obj);
                CurrentKey = null;
            }
            LastToken.Position = Index;
            LastToken.Token = '{';
            ++Column;
            ++Index;
            lock (classPropertyCacheLock)
            {
                if (!classPropertyCache.ContainsKey(targetType))
                {
                    classPropertyCache[targetType] = new Dictionary<string, PropertyInfo>();
                    foreach (PropertyInfo pi in targetType.GetProperties())
                    {
                        classPropertyCache[targetType].Add(pi.Name, pi);
                    }
                }
            }
        }

        void ProcessRCB()
        {
            if (LastToken == null)
            {
                CallException();
            }
            if (LastToken.Token == '[' || LastToken.Token == ',')// Any of '\"', '{', '}', ':' and ']' is fine
            {
                CallException();
            }
            if (IsDoubleProcessing)
            {
                SetValue(ObjectTypeStack.Peek(),ObjectCursor.Peek());
                IsDoubleProcessing = false;
                CurrentKey = null;
            }
            ObjectCursor.Pop();
            ObjectTypeStack.Pop();
            LastToken.Position = Index;
            LastToken.Token = '}';
            ++Column;
            ++Index;
        }

        void ProcessQuote()
        {
            if (LastToken == null || IsDoubleProcessing)
            {
                CallException();
            }
            if (LastToken.Token != '\"' && LastToken.Token != '{' && LastToken.Token != ',' && LastToken.Token != '[' && LastToken.Token != ':')
            {
                CallException();
            }

            if (LastToken.Token == ':') // Value start here "string":"value...
            {
                IsValue = true;
                IsInStringSegment = true;
            }
            else if (LastToken.Token == '\"') // "string" or "string":"value"
            {
                if (JSONStr[Index - 1] != '\\')//Not in string segment
                {
                    IsInStringSegment = false;
                    if (IsValue)
                    {
                        SetValue(ObjectTypeStack.Peek(), ObjectCursor.Peek());
                        IsValue = false;//Re-init this field
                    }
                    else
                    {
                        CurrentKey = JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position);
                    }
                }
                else// In string segment, just inc and go
                {
                    ++Index;
                    ++Column;
                    return;
                }
            }
            else// \" first appears in a key
            {
                IsInStringSegment = true;
                CurrentKey = null;
                IsValue = false;
            }
            LastToken.Position = Index;
            LastToken.Token = '\"';
            ++Index;
            ++Column;
            return;
        }

        void ProcessLSB()
        {
            Type targetType = null;
            Type parentType = null;
            object parent = null;
            //First element
            if (LastToken == null)
            {
                if (!CheckICollectionInterface(RootType))
                {
                    CallInvalidCastException();
                }
                else
                {
                    LastToken.Position = Index;
                    LastToken.Token = '[';
                    ++Index;
                    ++Column;
                    return;
                }
            }
            if (IsDoubleProcessing)
            {
                CallException();
            }
            if (LastToken.Token != '[' && LastToken.Token != ',' && LastToken.Token != ':' && LastToken.Token != '{')
            {
                CallException();
            }
            if (LastToken.Token == ':' && CurrentKey == null)//If json array is placed after colon, then there must be a key for this array
            {
                CallException();
            }
            parentType = ObjectTypeStack.Peek();
            parent = ObjectCursor.Peek();
            if (!classPropertyCache[parentType].ContainsKey(CurrentKey))
            {
                CallPropertyNotFoundException(parentType);
            }
            //Array is not supported right now;
            //Only Class which implements IList<T> is accepted
            if (classPropertyCache[parentType][CurrentKey].PropertyType.IsArray)
            {
                CallInvalidCastException();
            }
            targetType = classPropertyCache[parentType][CurrentKey].PropertyType;
            object obj = Activator.CreateInstance(targetType);
            
            if (!ICollectionArgumentCache.ContainsKey(targetType))
            {
                lock (ICollectionArgumentCacheLock)
                {
                    if (!ICollectionArgumentCache.ContainsKey(targetType))
                        ICollectionArgumentCache[targetType] = targetType.GetGenericArguments()[0];
                }
            }

            classPropertyCache[parentType][CurrentKey].SetValue(parent, obj, null);
            ObjectTypeStack.Push(targetType);
            ObjectCursor.Push(obj);
            LastToken.Position = Index;
            LastToken.Token = '[';
            ++Column;
            ++Index;
        }

        void ProcessRSB()
        {
            if (LastToken == null || LastToken.Token == '{' || LastToken.Token == ':') // Any of ','  '\"', '[', '}' and ']' is fine
            {
                CallException();
            }
            if ((LastToken.Token == ',' || LastToken.Token == '[') && IsDoubleProcessing) // OK, this is the last item of the list
            {
                Type curType = ObjectTypeStack.Peek();
                if (CheckICollectionInterface(curType))//Fine.. we will add the value
                {
                    if (ICollectionArgumentCache[curType].IsValueType)
                    {
                        AddICollectionValue(curType, ObjectCursor.Peek());
                    }
                    else
                    {
                        CallException();
                    }
                }
                else
                {
                    CallException();
                }
                IsDoubleProcessing = false;
            }
            ObjectCursor.Pop();
            ObjectTypeStack.Pop();
            LastToken.Position = Index;
            LastToken.Token = ']';
            ++Index;
            ++Column;
        }

        void ProcessColon()
        {
            if (LastToken == null || IsDoubleProcessing)
            {
                CallException();
            }
            if (LastToken.Token != '\"' && CurrentKey != null)// char ':' must be placed after a key
            {
                CallException();
            }
            LastToken.Token = ':';
            LastToken.Position = Index;
            ++Column;
            ++Index;

        }

        void ProcessBollean()
        {
            if (LastToken == null)
            {
                CallException();
            }
            if (IsDoubleProcessing)
            {
                CallException();
            }
            if (LastToken.Token != '[' && LastToken.Token != ',' && LastToken.Token != ':')
            {
                CallException();
            }
            if ((char.ToLowerInvariant(JSONStr[Index]) == 't'
                && char.ToLowerInvariant(JSONStr[Index + 1]) == 'r'
                && char.ToLowerInvariant(JSONStr[Index + 2]) == 'u'
                && char.ToLowerInvariant(JSONStr[Index + 3]) == 'e'
                )
                ||
            (char.ToLowerInvariant(JSONStr[Index]) == 'f'
                && char.ToLowerInvariant(JSONStr[Index + 1]) == 'a'
                && char.ToLowerInvariant(JSONStr[Index + 2]) == 'l'
                && char.ToLowerInvariant(JSONStr[Index + 3]) == 's'
                && char.ToLowerInvariant(JSONStr[Index + 4]) == 'e')
            )
            {
                //Check if ObjectType is generic
                if (CheckICollectionInterface(ObjectTypeStack.Peek()))
                {
                    if (ICollectionArgumentCache[ObjectTypeStack.Peek()] == typeof(bool))
                    {
                        if (char.ToLowerInvariant(JSONStr[Index]) == 't')
                        {
                            ((IList<bool>)ObjectCursor.Peek()).Add(true);
                            Index += 4;
                            Column += 4;
                        }
                        else
                        {
                            ((IList<bool>)ObjectCursor.Peek()).Add(false);
                            Index += 5;
                            Column += 5;
                        }
                    }
                    else
                    {
                        CallInvalidCastException();
                    }
                }
                else
                {
                    if (!classPropertyCache[ObjectTypeStack.Peek()].ContainsKey(CurrentKey))
                    {
                        CallPropertyNotFoundException(ObjectTypeStack.Peek());
                    }
                    if (char.ToLowerInvariant(JSONStr[Index]) == 't')
                    {
                        classPropertyCache[ObjectTypeStack.Peek()][CurrentKey].SetValue(ObjectCursor.Peek(), true, null);
                        Index += 4;
                        Column += 4;
                    }
                    else
                    {
                        classPropertyCache[ObjectTypeStack.Peek()][CurrentKey].SetValue(ObjectCursor.Peek(), false, null);
                        Index += 5;
                        Column += 5;
                    }
                }
            }
            else
            {
                CallException();
            }
        }

        void ProcessNULL()
        {
            if (LastToken == null)
            {
                CallException();
            }
            if (IsDoubleProcessing)
            {
                CallException();
            }
            if (LastToken.Token != '{' && LastToken.Token != '[' && LastToken.Token != ',' && LastToken.Token != ':')
            {
                CallException();
            }
            if (char.ToLowerInvariant(JSONStr[Index]) == 'n'
                && char.ToLowerInvariant(JSONStr[Index + 1]) == 'u'
                && char.ToLowerInvariant(JSONStr[Index + 2]) == 'l'
                && char.ToLowerInvariant(JSONStr[Index + 3]) == 'l'
                )
            {
                Type curType = ObjectTypeStack.Peek();
                if (CheckICollectionInterface(curType))
                {
                    if (ICollectionArgumentCache[curType].IsClass)//T of IList<T> must be reference type
                    {
                        //Call the add method
                        MethodInfo addMethod = null;
                        lock (methodCacheLock)
                        {
                            if (!methodCache.ContainsKey(curType))
                            {
                                methodCache[curType] = new Dictionary<string, MethodInfo>();
                                addMethod = curType.GetMethod("Add");
                                methodCache[curType]["Add"] = addMethod;
                            }
                            else
                            {
                                addMethod = methodCache[curType]["Add"];
                            }
                        }

                        addMethod.Invoke(ObjectCursor.Peek(), new object[] { null });
                        Index += 4;
                        Column += 4;
                    }
                    else
                    {
                        CallInvalidCastException();
                    }
                }
                else
                {
                    if (!classPropertyCache[ObjectTypeStack.Peek()].ContainsKey(CurrentKey))
                    {
                        CallPropertyNotFoundException(ObjectTypeStack.Peek());
                    }
                    classPropertyCache[ObjectTypeStack.Peek()][CurrentKey].SetValue(ObjectCursor.Peek(), null, null);
                    Index += 4;
                    Column += 4;
                }
            }
            else
            {
                CallException();
            }
        }

        /// <summary>
        /// This method actually process double or normal chars
        /// </summary>
        void ProcessDefault()
        {
            if (LastToken == null)
            {
                CallException();
            }
            if (LastToken.Token != ':' && LastToken.Token != ',' && LastToken.Token != '[' && LastToken.Token != '{')
            {
                CallException();
            }
            /*
             * Invalid chars will not be processed. Users should provide valid JSON data but not waiting the Parser to do everything for them
             */
            if (!IsDoubleProcessing)
            {
                IsDoubleProcessing = true;
            }
            ++Index;
            ++Column;
        }

        void ProcessComma()
        {
            if (LastToken == null)
            {
                CallException();
            }
            if (IsDoubleProcessing)
            {
                /*
                 * Well, the current obj is something like List<double>, check the T and uses the default parser
                 * Anyway, let's check the previous token first...
                 */
                if (LastToken.Token == '[' || LastToken.Token == ',' || LastToken.Token == ':')
                {
                    Type curType = ObjectTypeStack.Peek();
                    if (CurrentKey == null)//root or in object or in array
                    {
                        
                        if (CheckICollectionInterface(curType))//Fine.. we will add the value
                        {
                            AddICollectionValue(curType,ObjectCursor.Peek());
                        }
                        else
                        {
                            CallException();
                        }
                    }
                    else
                    {
                        if (CheckICollectionInterface(curType))//Fine.. we will add the value
                        {
                            AddICollectionValue(curType, ObjectCursor.Peek());
                        }
                        else
                        {
                            SetValue(curType, ObjectCursor.Peek());
                        }
                    }
                    LastToken.Token = ',';
                    LastToken.Position = Index;
                    ++Index;
                    ++Column;
                    CurrentKey = null;

                    IsDoubleProcessing = false;
                }
                else
                {
                    CallException();
                }
            }
            else
            {
                LastToken.Position = Index;
                LastToken.Token = ',';
                ++Index;
                ++Column;

                return;
            }
        }

        void AddICollectionValue(Type cachedType, Object obj)
        {
            try
            {
                switch (ICollectionArgumentCache[cachedType].Name)
                {
                    case "Byte":
                        ((ICollection<Byte>)obj).Add(Byte.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)));
                        break;
                    case "Int16":
                        ((ICollection<Int16>)obj).Add(Int16.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)));
                        break;
                    case "UInt16":
                        ((ICollection<UInt16>)obj).Add(UInt16.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)));
                        break;
                    case "Int32":
                        ((ICollection<Int32>)obj).Add(Int32.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)));
                        break;
                    case "UInt32":
                        ((ICollection<UInt32>)obj).Add(UInt32.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)));
                        break;
                    case "Int64":
                        ((ICollection<Int64>)obj).Add(Int64.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)));
                        break;
                    case "UInt64":
                        ((ICollection<UInt64>)obj).Add(UInt64.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)));
                        break;
                    case "bool":
                        ((ICollection<bool>)obj).Add(bool.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)));
                        break;
                    case "Decimal":
                        ((ICollection<Decimal>)obj).Add(Decimal.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)));
                        break;
                    case "Double":
                        ((ICollection<Double>)obj).Add(Double.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)));
                        break;
                    case "String":
                        ((ICollection<String>)obj).Add(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position));
                        break;
                    default:
                        if (ICollectionArgumentCache[cachedType].IsEnum)
                        {
                            MethodInfo addMethod = null;
                            lock (methodCacheLock)
                            {
                                if (!methodCache.ContainsKey(cachedType))
                                {
                                    methodCache[cachedType] = new Dictionary<string, MethodInfo>();
                                    addMethod = cachedType.GetMethod("Add");
                                    methodCache[cachedType]["Add"] = addMethod;
                                }
                                else
                                {
                                    addMethod = methodCache[cachedType]["Add"];
                                }
                            }
                            addMethod.Invoke(obj, new object[] { 
                        Enum.Parse(ICollectionArgumentCache[cachedType],
                        JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)) });
                            return;
                        }
                        else
                        {
                            CallUnSupportException();
                        }

                        break;
                }
            }
            catch (Exception e)
            {
                CallException(e);
            }
        }

        void SetValue(Type cachedType,object obj)
        {
            try
            {
                if (!classPropertyCache[cachedType].ContainsKey(CurrentKey))
                {
                    CallPropertyNotFoundException(cachedType);
                }
                if (classPropertyCache[cachedType][CurrentKey].PropertyType.IsEnum)
                {
                    classPropertyCache[cachedType][CurrentKey].SetValue(obj, Enum.Parse(classPropertyCache[cachedType][CurrentKey].PropertyType, JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)), null);
                    return;
                }
                switch (classPropertyCache[cachedType][CurrentKey].PropertyType.Name)
                {
                    case "Byte":
                        classPropertyCache[cachedType][CurrentKey].SetValue(obj, Byte.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)), null);
                        break;
                    case "Int16":
                        classPropertyCache[cachedType][CurrentKey].SetValue(obj, Int16.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)), null);
                        break;
                    case "UInt16":
                        classPropertyCache[cachedType][CurrentKey].SetValue(obj, UInt16.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)), null);
                        break;
                    case "Int32":
                        classPropertyCache[cachedType][CurrentKey].SetValue(obj, Int32.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)), null);
                        break;
                    case "UInt32":
                        classPropertyCache[cachedType][CurrentKey].SetValue(obj, UInt32.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)), null);
                        break;
                    case "Int64":
                        classPropertyCache[cachedType][CurrentKey].SetValue(obj, Int64.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)), null);
                        break;
                    case "UInt64":
                        classPropertyCache[cachedType][CurrentKey].SetValue(obj, UInt64.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)), null);
                        break;
                    case "bool":
                        classPropertyCache[cachedType][CurrentKey].SetValue(obj, Boolean.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)), null);
                        break;
                    case "Decimal":
                        classPropertyCache[cachedType][CurrentKey].SetValue(obj, Decimal.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)), null);
                        break;
                    case "Double":
                        classPropertyCache[cachedType][CurrentKey].SetValue(obj, Double.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)), null);
                        break;
                    case "String":
                        classPropertyCache[cachedType][CurrentKey].SetValue(obj, JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position), null);
                        break;
                    case "DateTime":
                        classPropertyCache[cachedType][CurrentKey].SetValue(obj, DateTime.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)), null);
                        break;
                    case "TimeSpan":
                        classPropertyCache[cachedType][CurrentKey].SetValue(obj, TimeSpan.Parse(JSONStr.Substring(LastToken.Position + 1, Index - 1 - LastToken.Position)), null);
                        break;
                    default:
                        CallUnSupportException();
                        break;
                }

            }
            catch (Exception e)
            {
                CallException(e);
            }
        }

        void CallException()
        {
            throw new Exception(string.Format(ErrorMSG, Line, Column));
        }

        void CallException(Exception innerException)
        {
            throw new Exception(string.Format(ErrorMSG, Line, Column), innerException);
        }

        void CallPropertyNotFoundException(Type parentType)
        {
            throw new Exception(string.Format(PropertyNotFoundMSG, CurrentKey, parentType));
        }

        void CallInvalidCastException()
        {
            throw new InvalidCastException(string.Format(ErrorMSG, Line, Column));
        }

        void CallUnSupportException()
        {
            throw new NotSupportedException(string.Format(UnsupportErrorMSG, CurrentKey, ObjectTypeStack.Peek().Name));
        }

        bool CheckIDictionaryInterface(Type targetType)
        {
            if (!targetType.IsGenericType)
            {
                return false;
            }
            if (!IDictionaryArgumentCache.ContainsKey(targetType))
            {
                Type[] intefaces = targetType.GetInterfaces();
                foreach (Type item in intefaces)
                {
                    if (item.Name == IDictionaryGeneric.Name)
                    {
                        lock (IDictionaryArgumentCacheLock)
                        {
                            if (!IDictionaryArgumentCache.ContainsKey(targetType))
                            {
                                Type[] types = targetType.GetGenericArguments();
                                IDictionaryArgumentCache[targetType] = new KeyValuePair<Type, Type>(types[0], types[1]);
                            }
                        }
                        return true;
                    }
                }
                return false;
            }
            return true;
        }

        bool CheckICollectionInterface(Type targetType)
        {
            if (!targetType.IsGenericType)
            {
                return false;
            }
            if (!ICollectionArgumentCache.ContainsKey(targetType))
            {
                Type[] ts = targetType.GetInterfaces();
                foreach (Type item in ts)
                {
                    if (item.Name == ICollection.Name)
                    {
                        lock (ICollectionArgumentCacheLock)
                        {
                            if (!ICollectionArgumentCache.ContainsKey(targetType))
                            {
                                ICollectionArgumentCache[targetType] = targetType.GetGenericArguments()[0];
                            }
                        }
                        return true;
                    }
                }
                return false;
            }
            return true;
        }
    }

    internal class Cursor
    {
        public int Position = 0;
        public char Token = '\0';

        public static Cursor New(int i, char ch)
        {
            Cursor cur = new Cursor();
            cur.Position = i;
            cur.Token = ch;
            return cur;
        }
    }
}
