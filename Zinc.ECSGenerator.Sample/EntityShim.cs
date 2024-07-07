namespace Zinc.Core;

public class BaseEntity {}
public class BaseComponentAttribute : System.Attribute {}
public class UseNestedComponentMemberNamesAttribute : System.Attribute {}
public interface IComponent {}

public class ComponentAttribute<T>(string name = "") : System.Attribute where T : IComponent {}