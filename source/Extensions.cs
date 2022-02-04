using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using CustomAttributeNamedArgument = Mono.Cecil.CustomAttributeNamedArgument;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using ICustomAttributeProvider = Mono.Cecil.ICustomAttributeProvider;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace BetterCecil
{
    public static class Extensions
    {
        public static bool Is(this TypeReference td, Type t)
        {
            if (t.IsGenericType)
            {
                return td.GetElementType().FullName == t.FullName;
            }
            return td.FullName == t.FullName;
        }

        public static bool Is<T>(this TypeReference td) => Is(td, typeof(T));

        public static bool Is(this MethodReference method, Type t, string name) =>
            method.DeclaringType.Is(t) && method.Name == name;

        public static bool Is<T>(this MethodReference method, string name) =>
            method.DeclaringType.Is<T>() && method.Name == name;

        public static bool IsDerivedFrom<T>(this TypeDefinition td) => IsDerivedFrom(td, typeof(T));

        public static bool IsDerivedFrom(this TypeDefinition td, Type baseClass)
        {
            if (td == null)
                return false;

            if (!td.IsClass)
                return false;

            // are ANY parent classes of baseClass?
            TypeReference parent = td.BaseType;

            if (parent == null)
                return false;

            if (parent.Is(baseClass))
                return true;

            if (parent.CanBeResolved())
                return IsDerivedFrom(parent.Resolve(), baseClass);

            return false;
        }

        /// <summary>
        /// Resolves type using try/catch check
        /// Replacement for <see cref="CanBeResolved(TypeReference)"/>
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static TypeDefinition TryResolve(this TypeReference type)
        {
            if (type.Scope.Name == "Windows")
            {
                return null;
            }

            try
            {
                return type.Resolve();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Uses <see cref="TryResolve(TypeReference)"/> to find the Base Type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static TypeReference TryResolveParent(this TypeReference type)
        {
            return type.TryResolve()?.BaseType;
        }

        // set the value of a constant in a class
        public static void SetConst<T>(this TypeDefinition td, string fieldName, T value) where T : struct
        {
            FieldDefinition field = td.Fields.FirstOrDefault(f => f.Name == fieldName);

            if (field == null)
            {
                field = new FieldDefinition(fieldName, FieldAttributes.Literal | FieldAttributes.NotSerialized | FieldAttributes.Private, td.Module.ImportReference<T>());
                td.Fields.Add(field);
            }

            field.Constant = value;
        }

        public static T GetConst<T>(this TypeDefinition td, string fieldName) where T : struct
        {
            FieldDefinition field = td.Fields.FirstOrDefault(f => f.Name == fieldName);

            if (field == null)
            {
                return default;
            }

            var value = field.Constant as T?;

            return value.GetValueOrDefault();
        }

        public static bool ImplementsInterface<TInterface>(this TypeDefinition td)
        {
            if (td == null)
                return false;

            if (td.Is<TInterface>())
                return true;

            TypeDefinition typedef = td;

            while (typedef != null)
            {
                foreach (InterfaceImplementation iface in typedef.Interfaces)
                {
                    if (iface.InterfaceType.Is<TInterface>())
                        return true;
                }

                try
                {
                    TypeReference parent = typedef.BaseType;
                    typedef = parent?.Resolve();
                }
                catch (AssemblyResolutionException)
                {
                    // this can happen for pluins.
                    break;
                }
            }

            return false;
        }

        public static bool IsMultidimensionalArray(this TypeReference tr)
        {
            return tr is ArrayType arrayType && arrayType.Rank > 1;
        }

        public static bool CanBeResolved(this TypeReference parent)
        {
            while (parent != null)
            {
                if (parent.Scope.Name == "Windows")
                {
                    return false;
                }

                if (parent.Scope.Name == "mscorlib")
                {
                    TypeDefinition resolved = parent.Resolve();
                    return resolved != null;
                }

                try
                {
                    parent = parent.Resolve().BaseType;
                }
                catch
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Given a method of a generic class such as ArraySegment`T.get_Count,
        /// and a generic instance such as ArraySegment`int
        /// Creates a reference to the specialized method  ArraySegment`int`.get_Count
        /// <para> Note that calling ArraySegment`T.get_Count directly gives an invalid IL error </para>
        /// </summary>
        /// <param name="self"></param>
        /// <param name="instanceType"></param>
        /// <returns></returns>
        public static MethodReference MakeHostInstanceGeneric(this MethodReference self, GenericInstanceType instanceType)
        {
            var reference = new MethodReference(self.Name, self.ReturnType, instanceType)
            {
                CallingConvention = self.CallingConvention,
                HasThis = self.HasThis,
                ExplicitThis = self.ExplicitThis
            };

            foreach (ParameterDefinition parameter in self.Parameters)
                reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));

            foreach (GenericParameter generic_parameter in self.GenericParameters)
                reference.GenericParameters.Add(new GenericParameter(generic_parameter.Name, reference));

            return self.Module.ImportReference(reference);
        }

        public static bool TryGetCustomAttribute<TAttribute>(this ICustomAttributeProvider method, out CustomAttribute customAttribute)
        {
            foreach (CustomAttribute ca in method.CustomAttributes)
            {
                if (ca.AttributeType.Is<TAttribute>())
                {
                    customAttribute = ca;
                    return true;
                }
            }

            customAttribute = null;
            return false;
        }

        public static CustomAttribute GetCustomAttribute<TAttribute>(this ICustomAttributeProvider method)
        {
            _ = method.TryGetCustomAttribute<TAttribute>(out CustomAttribute customAttribute);
            return customAttribute;
        }

        public static bool HasCustomAttribute<TAttribute>(this ICustomAttributeProvider attributeProvider)
        {
            return HasCustomAttribute(attributeProvider, typeof(TAttribute));
        }

        public static bool HasCustomAttribute(this ICustomAttributeProvider attributeProvider, Type t)
        {
            return attributeProvider.CustomAttributes.Any(attr => attr.AttributeType.Is(t));
        }

        public static T GetField<T>(this CustomAttribute ca, string field, T defaultValue)
        {
            foreach (CustomAttributeNamedArgument customField in ca.Fields)
            {
                if (customField.Name == field)
                {
                    return (T)customField.Argument.Value;
                }
            }

            return defaultValue;
        }

        public static FieldReference MakeHostGenericIfNeeded(this FieldReference fd)
        {
            if (fd.DeclaringType.HasGenericParameters)
            {
                return new FieldReference(fd.Name, fd.FieldType, fd.DeclaringType.Resolve().ConvertToGenericIfNeeded());
            }

            return fd;
        }
    }

    public static class MethodExtensions
    {
        // todo add documentation 
        public static ParameterDefinition AddParam<T>(this MethodDefinition method, string name, ParameterAttributes attributes = ParameterAttributes.None)
            => AddParam(method, method.Module.ImportReference(typeof(T)), name, attributes);

        // todo add documentation 
        public static ParameterDefinition AddParam(this MethodDefinition method, TypeReference typeRef, string name, ParameterAttributes attributes = ParameterAttributes.None)
        {
            var param = new ParameterDefinition(name, attributes, typeRef);
            method.Parameters.Add(param);
            return param;
        }

        // todo add documentation 
        public static VariableDefinition AddLocal<T>(this MethodDefinition method) => AddLocal(method, method.Module.ImportReference(typeof(T)));

        // todo add documentation 
        public static VariableDefinition AddLocal(this MethodDefinition method, TypeReference type)
        {
            var local = new VariableDefinition(type);
            method.Body.Variables.Add(local);
            return local;
        }

        // todo add documentation 
        public static Instruction Create(this ILProcessor worker, OpCode code, LambdaExpression expression)
        {
            MethodReference typeref = worker.Body.Method.Module.ImportReference(expression);
            return worker.Create(code, typeref);
        }

        // todo add documentation 
        public static Instruction Create(this ILProcessor worker, OpCode code, Expression<Action> expression)
        {
            MethodReference typeref = worker.Body.Method.Module.ImportReference(expression);
            return worker.Create(code, typeref);
        }

        // todo add documentation 
        public static Instruction Create<T>(this ILProcessor worker, OpCode code, Expression<Action<T>> expression)
        {
            MethodReference typeref = worker.Body.Method.Module.ImportReference(expression);
            return worker.Create(code, typeref);
        }

        // todo add documentation 
        public static Instruction Create<T, TR>(this ILProcessor worker, OpCode code, Expression<Func<T, TR>> expression)
        {
            MethodReference typeref = worker.Body.Method.Module.ImportReference(expression);
            return worker.Create(code, typeref);
        }

        public static SequencePoint GetSequencePoint(this MethodDefinition method, Instruction instruction)
        {
            SequencePoint sequencePoint = method.DebugInformation.GetSequencePoint(instruction);
            return sequencePoint;
        }
    }

    public static class ModuleExtension
    {
        public static MethodReference ImportReference(this ModuleDefinition module, Expression<Action> expression) => ImportReference(module, (LambdaExpression)expression);

        /// <summary>
        /// this can be used to import reference to a non-static method
        /// <para>
        /// for example, <code>(NetworkWriter writer) => writer.Write(default, default)</code>
        /// </para>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="module"></param>
        /// <param name="expression"></param>
        /// <returns></returns>
        public static MethodReference ImportReference<T>(this ModuleDefinition module, Expression<Action<T>> expression) => ImportReference(module, (LambdaExpression)expression);
        public static MethodReference ImportReference<T1, T2>(this ModuleDefinition module, Expression<Func<T1, T2>> expression) => ImportReference(module, (LambdaExpression)expression);

        public static TypeReference ImportReference<T>(this ModuleDefinition module) => module.ImportReference(typeof(T));

        public static MethodReference ImportReference(this ModuleDefinition module, LambdaExpression expression)
        {
            if (expression.Body is MethodCallExpression outermostExpression)
            {
                MethodInfo methodInfo = outermostExpression.Method;
                return module.ImportReference(methodInfo);
            }

            if (expression.Body is NewExpression newExpression)
            {
                ConstructorInfo methodInfo = newExpression.Constructor;
                // constructor is null when creating an ArraySegment<object>
                methodInfo = methodInfo ?? newExpression.Type.GetConstructors()[0];
                return module.ImportReference(methodInfo);
            }

            if (expression.Body is MemberExpression memberExpression)
            {
                var property = memberExpression.Member as PropertyInfo;
                return module.ImportReference(property.GetMethod);
            }

            throw new ArgumentException($"Invalid Expression {expression.Body.GetType()}");
        }


        public static TypeDefinition GeneratedClass(this ModuleDefinition module)
        {
            TypeDefinition type = module.GetType("Mirage", "GeneratedNetworkCode");

            if (type != null)
                return type;

            type = new TypeDefinition("Mirage", "GeneratedNetworkCode",
                        TypeAttributes.BeforeFieldInit | TypeAttributes.Class | TypeAttributes.AnsiClass | TypeAttributes.Public | TypeAttributes.AutoClass | TypeAttributes.Abstract | TypeAttributes.Sealed,
                        module.ImportReference<object>());
            module.Types.Add(type);
            return type;
        }
    }

    public static class TypeExtensions
    {

        public static MethodDefinition GetMethod(this TypeDefinition td, string methodName)
        {
            // Linq allocations don't matter in weaver
            return td.Methods.FirstOrDefault(method => method.Name == methodName);
        }

        public static MethodDefinition[] GetMethods(this TypeDefinition td, string methodName)
        {
            // Linq allocations don't matter in weaver
            return td.Methods.Where(method => method.Name == methodName).ToArray();
        }

        /// <summary>
        /// Finds a method in base type
        /// <para>
        /// IMPORTANT: dont resolve <paramref name="typeReference"/> before calling this or methods can not be made into generic methods
        /// </para>
        /// </summary>
        /// <param name="typeReference">Unresolved type reference, dont resolve if generic</param>
        /// <param name="methodName"></param>
        /// <returns></returns>
        public static MethodReference GetMethodInBaseType(this TypeReference typeReference, string methodName)
        {
            return GetMethodInBaseType(typeReference, (md) => md.Name == methodName);
        }

        /// <summary>
        /// Finds a method in base type
        /// <para>
        /// IMPORTANT: dont resolve <paramref name="typeReference"/> before calling this or methods can not be made into generic methods
        /// </para>
        /// </summary>
        /// <param name="typeReference">Unresolved type reference, dont resolve if generic</param>
        /// <param name="methodName"></param>
        /// <returns></returns>
        public static MethodReference GetMethodInBaseType(this TypeReference typeReference, Predicate<MethodDefinition> match)
        {
            TypeDefinition typedef = typeReference.Resolve();
            TypeReference typeRef = typeReference;
            while (typedef != null)
            {
                foreach (MethodDefinition md in typedef.Methods)
                {
                    if (match.Invoke(md))
                    {
                        MethodReference method = md;
                        if (typeRef.IsGenericInstance)
                        {
                            var generic = (GenericInstanceType)typeRef;
                            method = method.MakeHostInstanceGeneric(generic);
                        }

                        return method;
                    }
                }

                try
                {
                    TypeReference parent = typedef.BaseType;
                    if (parent.IsGenericInstance)
                    {
                        parent = MatchGenericParameters((GenericInstanceType)parent, typeRef);
                    }
                    typeRef = parent;
                    typedef = parent?.Resolve();
                }
                catch (AssemblyResolutionException)
                {
                    // this can happen for plugins.
                    break;
                }
            }

            return null;
        }

        /// <summary>
        /// Takes generic argments from child class and applies them to base class
        /// <br/>
        /// eg makes `Base{T}` in <c>Child{int} : Base{int}</c> have `int` instead of `T`
        /// </summary>
        /// <param name="parentReference"></param>
        /// <param name="childReference"></param>
        /// <returns></returns>
        public static GenericInstanceType MatchGenericParameters(this GenericInstanceType parentReference, TypeReference childReference)
        {
            if (!parentReference.IsGenericInstance)
                throw new InvalidOperationException("Can't make non generic type into generic");

            // make new type so we can replace the args on it
            // resolve it so we have non-generic instance (eg just instance with <T> instead of <int>)
            // if we dont cecil will make it double generic (eg INVALID IL)
            var generic = new GenericInstanceType(parentReference.Resolve());
            foreach (TypeReference arg in parentReference.GenericArguments)
                generic.GenericArguments.Add(arg);

            for (int i = 0; i < generic.GenericArguments.Count; i++)
            {
                // if arg is not generic
                // eg List<int> would be int so not generic.
                // But List<T> would be T so is generic
                if (!generic.GenericArguments[i].IsGenericParameter)
                    continue;

                // get the generic name, eg T
                string name = generic.GenericArguments[i].Name;
                // find what type T is, eg turn it into `int` if `List<int>`
                TypeReference arg = FindMatchingGenericArgument(childReference, name);

                // import just to be safe
                TypeReference imported = parentReference.Module.ImportReference(arg);
                // set arg on generic, parent ref will be Base<int> instead of just Base<T>
                generic.GenericArguments[i] = imported;
            }

            return generic;

        }
        static TypeReference FindMatchingGenericArgument(TypeReference childReference, string paramName)
        {
            TypeDefinition def = childReference.Resolve();
            // child class must be generic if we are in this part of the code
            // eg Child<T> : Base<T>  <--- child must have generic if Base has T
            // vs Child : Base<int> <--- wont be here if Base has int (we check if T exists before calling this)
            if (!def.HasGenericParameters)
                throw new InvalidOperationException("Base class had generic parameters, but could not find them in child class");

            // go through parameters in child class, and find the generic that matches the name
            for (int i = 0; i < def.GenericParameters.Count; i++)
            {
                GenericParameter param = def.GenericParameters[i];
                if (param.Name == paramName)
                {
                    var generic = (GenericInstanceType)childReference;
                    // return generic arg with same index
                    return generic.GenericArguments[i];
                }
            }

            // this should never happen, if it does it means that this code is bugged
            throw new InvalidOperationException("Did not find matching generic");
        }

        /// <summary>
        /// Finds public fields in type and base type
        /// </summary>
        /// <param name="variable"></param>
        /// <returns></returns>
        public static IEnumerable<FieldDefinition> FindAllPublicFields(this TypeReference variable)
        {
            return FindAllPublicFields(variable.Resolve());
        }

        /// <summary>
        /// Finds public fields in type and base type
        /// </summary>
        /// <param name="variable"></param>
        /// <returns></returns>
        public static IEnumerable<FieldDefinition> FindAllPublicFields(this TypeDefinition typeDefinition)
        {
            while (typeDefinition != null)
            {
                foreach (FieldDefinition field in typeDefinition.Fields.ToArray())
                {
                    if (field.IsStatic || field.IsPrivate)
                        continue;

                    if (field.IsNotSerialized)
                        continue;

                    yield return field;
                }

                try
                {
                    typeDefinition = typeDefinition.BaseType?.Resolve();
                }
                catch
                {
                    break;
                }
            }
        }

        public static TypeDefinition AddType(this TypeDefinition typeDefinition, string name, TypeAttributes typeAttributes, bool valueType) =>
            AddType(typeDefinition, name, typeAttributes, valueType ? typeDefinition.Module.ImportReference(typeof(ValueType)) : null);
        public static TypeDefinition AddType(this TypeDefinition typeDefinition, string name, TypeAttributes typeAttributes, TypeReference baseType)
        {
            var type = new TypeDefinition("", name, typeAttributes, baseType)
            {
                DeclaringType = typeDefinition
            };
            typeDefinition.NestedTypes.Add(type);
            return type;
        }

        public static MethodDefinition AddMethod(this TypeDefinition typeDefinition, string name, MethodAttributes attributes, TypeReference returnType)
        {
            var method = new MethodDefinition(name, attributes, returnType);
            typeDefinition.Methods.Add(method);
            return method;
        }

        public static MethodDefinition AddMethod(this TypeDefinition typeDefinition, string name, MethodAttributes attributes) =>
            AddMethod(typeDefinition, name, attributes, typeDefinition.Module.ImportReference(typeof(void)));

        public static FieldDefinition AddField<T>(this TypeDefinition typeDefinition, string name, FieldAttributes attributes) =>
            AddField(typeDefinition, typeDefinition.Module.ImportReference(typeof(T)), name, attributes);

        public static FieldDefinition AddField(this TypeDefinition typeDefinition, TypeReference fieldType, string name, FieldAttributes attributes)
        {
            var field = new FieldDefinition(name, attributes, fieldType);
            field.DeclaringType = typeDefinition;
            typeDefinition.Fields.Add(field);
            return field;
        }

        /// <summary>
        /// Creates a generic type out of another type, if needed.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static TypeReference ConvertToGenericIfNeeded(this TypeDefinition type)
        {
            if (type.HasGenericParameters)
            {
                // get all the generic parameters and make a generic instance out of it
                var genericTypes = new TypeReference[type.GenericParameters.Count];
                for (int i = 0; i < type.GenericParameters.Count; i++)
                {
                    genericTypes[i] = type.GenericParameters[i].GetElementType();
                }

                return type.MakeGenericInstanceType(genericTypes);
            }
            else
            {
                return type;
            }
        }

        public static FieldReference GetField(this TypeDefinition type, string fieldName)
        {
            if (type.HasFields)
            {
                for (int i = 0; i < type.Fields.Count; i++)
                {
                    if (type.Fields[i].Name == fieldName)
                    {
                        return type.Fields[i];
                    }
                }
            }

            return null;
        }
    }
}
