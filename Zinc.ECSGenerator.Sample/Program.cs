using Zinc.Core;
using System;

namespace Zinc.ECSGeneratorSample;

class Program
{
    static void Main(string[] args)
    {
        // Your code here
        System.Console.WriteLine("Hello, World!");
        var e = new TestEntity(){ X = 1, Y = 2, Z = 3, CircleCollider_X = 10, ID = 99 };
        e.OnValueChanged += (v) => System.Console.WriteLine($"Value change invoked: {v}"); 
        System.Console.WriteLine($"ID: {e.ID} X: {e.X}, Y: {e.Y}");
        e.OnValueChanged?.Invoke(42);

        var d = new DerrivedClass();
        d.derrivedValue = 42;
    }

}

// note - you can use struct and record struct fine in prod
// we just use class here to satisfy the entity shim
// (such that we dont need arch as a direct dep for the sample project)
public class Position : IComponent 
{ 
    public float X { get; set; } 
    public float Y { get; set; } 
    public float Z { get; set; } 
    public Action<float> OnValueChanged { get; set; }
}

public class Collider : IComponent 
{ 
    public float X { get; set; } 
    public float Y { get; set; } 
}

public class DerrivedComponent : IComponent
{
    public float derrivedValue { get; set; }
}

public class BaseClassComponent : IComponent
{
    public int ID { get; set; }
}

[Component<BaseClassComponent>()]
public partial class EntityBase
{
    //test to make sure we can have a base class with components
    //shim the real entity class
    public ShimECSEntity ECSEntity = new();
}

public class Entity : EntityBase
{
    protected virtual void AddAttributeComponents()
    {

    }
}

// [UseNestedComponentMemberNames]
[Component<Position>()]
[Component<Collider>("CircleCollider")]
public partial class TestEntity : Entity
{
    //test that we can do derrived classes
}

[Component<DerrivedComponent>()]
public partial class DerrivedClass : TestEntity
{
    //test that we can do children of children
}
