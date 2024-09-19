using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Zinc.Core;

public class ShimECSEntity
{
    private Dictionary<Type, object> components = new();
    public ShimECSEntity()
    {
        //to spoof this so the ref works (without needing to download arch)
        //we prepopulate a list of bogus components that we point to

        var componentTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        foreach (var type in componentTypes)
        {
            components[type] = Activator.CreateInstance(type)!;
        }

    }

    public void Add(params object[] args)
    {
        return;
    }
    
    public ref T Get<T>() where T : class, IComponent
    {
        unsafe
        {
            if (!components.TryGetValue(typeof(T), out object component))
            {
                throw new InvalidOperationException($"Component of type {typeof(T)} not found.");
            }
            return ref System.Runtime.CompilerServices.Unsafe.As<object, T>(ref component);
        }
    }

    public void Set(params object[] args)
    {
        return;
    }
}

public static class ECSEntityReference
{
    public static class Entity
    {
        public static void Set(params object[] args)
        {
            return;
        }
    }
}
public class BaseComponentAttribute : System.Attribute {}
public class UseNestedComponentMemberNamesAttribute : System.Attribute {}
public interface IComponent {}
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
public class ComponentAttribute<T>(string name = "") : System.Attribute where T : IComponent {}