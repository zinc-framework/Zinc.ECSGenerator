using System;

namespace Arch.Core.Extensions
{
    
}

namespace Arch.Core.Utils
{
    
}

namespace Arch.Core
{
    public class ComponentType
    {
        private Type _type;

        public ComponentType(Type type)
        {
            _type = type;
        }

        public static implicit operator ComponentType(Type type)
        {
            return new ComponentType(type);
        }

        public static implicit operator Type(ComponentType componentType)
        {
            return componentType._type;
        }
    }
    public class Entity {}
    public class World
    {
        public Entity Create(ComponentType[] archetype) => new Entity();
    }
}