using Zinc.Core;

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
public class Position(bool nestTypeName = false)  : BaseComponentAttribute(nestTypeName)
{
    public float X { get; set; }
    public float Y { get; set; }
}

[Position(nestTypeName:true)]
public partial class TestEntity : BaseEntity
{
    
}
