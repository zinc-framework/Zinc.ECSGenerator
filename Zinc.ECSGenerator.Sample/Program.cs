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
    public float X { get; set; } = 1;
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

public record struct TestPrimaryCtorComponent(int recordStructValue = 10) : IComponent;
public readonly record struct TestPrimaryCtorComponent2(int something, int readonlyRecordStructValue = 11) : IComponent;
public record TestPrimaryCtorComponent3(int myVal, int recordValue = 12) : IComponent
{
    public static int StaticValue = 33;
    //TODO: need to seperate the idea here of ctor args and member values
    //member values should be set in {} after constrcution in object whereas ctor args for primary ctor should be set in the ctor
    public int Ctor3MemberValue = 42;
    public TestPrimaryCtorComponent3() : this(13)
    {
    }
}

public class AccessorComponentTester : IComponent
{
    public int PublicFieldValue = 3;
    private int PrivateFieldValue = 3;
    public int PublicProperty { get; set; } = 4;
    public int NoSetter { get; } = 4;
    public int PrivateGetter { private get; set; } = 4;
    public int ProtectedGetter { protected get; set; } = 4;
    public int PrivateSetter { get; private set; } = 4;
    public int ProtectedSetter { get; protected set; } = 4;
}

[Component<BaseClassComponent>()]
public partial class Entity
{
    //test to make sure we can have a base class with components
    //shim the real entity class
    public ShimECSEntity ECSEntity = new();
}

[Component<AccessorComponentTester>]
// [Component<TestPrimaryCtorComponent>] //uncomment this to test gen, but wont compile because entity shim requires a ref type for component
// [Component<TestPrimaryCtorComponent2>] //uncomment this to test gen, but wont compile because entity shim requires a ref type for component
[Component<TestPrimaryCtorComponent3>] //uncomment this to test gen, but wont compile because entity shim requires a ref type for component
public partial class AccessorTesterEntity : Entity
{

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
