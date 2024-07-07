namespace Zinc.Core;

public class BaseEntity
{

}

public class BaseComponentAttribute : System.Attribute
{
    public bool NestTypeName { get; private set; }
    public BaseComponentAttribute(bool nestTypeName = false)
    {
        NestTypeName = nestTypeName;
    }
} 