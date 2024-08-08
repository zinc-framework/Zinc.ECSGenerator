using Zinc.Core;
using System;

namespace Zinc.ECSGeneratorSample;

class Program
{
    static void Main(string[] args)
    {
        // Your code here
        System.Console.WriteLine("Hello, World!");
        var e = new TestEntity(){ X = 1, Y = 2, Z = 3, CircleCollider.X = 10 };
        e.OnValueChanged += (v) => System.Console.WriteLine($"Value change invoked: {v}"); 
        System.Console.WriteLine($"X: {e.X}, Y: {e.Y}");
        e.OnValueChanged?.Invoke(42);
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

// [UseNestedComponentMemberNames]
[Component<Position>(topLevelAccessor:true)]
[Component<Collider>("CircleCollider")]
public partial class TestEntity : Entity
{
    
}
