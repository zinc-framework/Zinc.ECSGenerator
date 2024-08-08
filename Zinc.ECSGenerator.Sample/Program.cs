using Zinc.Core;
using System;

namespace Zinc.ECSGeneratorSample;

class Program
{
    static void Main(string[] args)
    {
        // Your code here
        System.Console.WriteLine("Hello, World!");
        var e = new TestEntity(){ X = 1, Y = 2, Z = 3 };
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

// [UseNestedComponentMemberNames]
[Component<Position>("SomeCoolPosition")]
public partial class TestEntity : BaseEntity
{
    
}
