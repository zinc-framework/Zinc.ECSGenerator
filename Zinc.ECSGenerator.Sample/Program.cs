using Zinc.Core;
using System;

namespace Zinc.ECSGeneratorSample;

class Program
{
    static void Main(string[] args)
    {
        // Your code here
        System.Console.WriteLine("Hello, World!");
        var e = new TestEntity(){ X = 1, Y = 2 };
        System.Console.WriteLine($"X: {e.X}, Y: {e.Y}");
    }

}
public class Position  : BaseComponentAttribute
{
    public float X { get; set; }
    public float Y { get; set; }
}

[Position]
// [UseNestedComponentMemberNames]
public partial class TestEntity : BaseEntity
{
    
}
