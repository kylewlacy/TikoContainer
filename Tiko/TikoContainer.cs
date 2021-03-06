using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Tiko
{
	public static class TikoContainer
	{
		private static List<RegisteredObject> _registeredObjects =
			new List<RegisteredObject> ();

		/// <summary>
		/// Resolve dependencies on existing object.
		/// </summary>
		/// <typeparam name="T">Type of object.</typeparam>
		/// <param name="existing">Instance of the object.</param>
		/// <exception cref="DependencyMissingException">Throw exception if unable to resove a dependency.</exception>
		/// <returns>Result object.</returns>
		public static T BuildUp<T> (T existing)
		{
			return DoBuildUp (existing);
		}

		/// <summary>
		/// Clear the container.
		/// </summary>
		public static void Clear ()
		{
			_registeredObjects = new List<RegisteredObject> ();
		}

		/// <summary>
		/// Register class.
		/// </summary>
		/// <typeparam name="T">Type of class.</typeparam>
		public static void Register<T> ()
            where T : new()
		{
			Register<T, T> ();
		}

		/// <summary>
		/// Register object.
		/// </summary>
		/// <typeparam name="TFrom">That will be requested.</typeparam>
		/// <typeparam name="TTo">That will actually be returned.</typeparam>
		public static void Register<TFrom, TTo> ()
            where TTo : TFrom, new()
		{
			var registeredObject = new RegisteredObject (() => new TTo (), typeof(TFrom));
			_registeredObjects.Add (registeredObject);
		}

		private static object Register (Type from, Type to)
		{
			object instance = to.GetConstructor (new Type[] { }).Invoke (new object[] { });
			var registeredObject = new RegisteredObject (() => instance, from);
			_registeredObjects.Add (registeredObject);

			return instance;
		}

		/// <summary>
		/// Resolve an instance and dependencies.
		/// </summary>
		/// <typeparam name="T">Type of object.</typeparam>
		/// <exception cref="DependencyMissingException">Throw exception if unable to resove a dependency.</exception>
		/// <returns>Result object.</returns>
		public static T Resolve<T> ()
		{
			T instance;
			object resolvedObject;
			if (ResolveObject (typeof(T), out resolvedObject) || ResolveAttribute (typeof(T), out resolvedObject))
			{
				instance = (T)resolvedObject;
			} else {
				instance = Activator.CreateInstance<T> ();
			}
			return DoBuildUp (instance);
		}

		private static T DoBuildUp<T> (T instance)
		{
			PropertyInfo[] properties = typeof(T).GetProperties ();
			foreach (PropertyInfo property in properties) {
				if (!IsResolveProperty (property)) {
					continue;
				}
				object resolvedObject;
				bool isResolved = ResolveObject (property.PropertyType, out resolvedObject);
				if (!isResolved) {
					throw new DependencyMissingException (string.Format (
						"Could not resolve dependency for {0}", typeof(T).Name));
				}
				property.SetValue (instance, resolvedObject, null);
			}
			return instance;
		}

		private static bool IsResolveProperty (PropertyInfo property)
		{
			object[] attributes = property.GetCustomAttributes (typeof(DependencyAttribute), false);
			return attributes.Length != 0;
		}

		private static bool ResolveObject (Type typeToResolve, out object resolvedObject)
		{
			RegisteredObject result = _registeredObjects.FirstOrDefault (x => x.ObjectType == typeToResolve);
			if (result == null) {
				resolvedObject = null;
				return false;
			}
			resolvedObject = result.Instance;
			return true;
		}

		private static bool ResolveAttribute (Type typeToResolve, out object resolvedObject)
		{
			Type resolvedType = null;
			foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
				// Instantiates `UsesAttribute`s, which forces used types to load at runtime
				assembly.GetCustomAttributes(typeof(UsesAttribute), true);

				try {
					foreach (Type type in assembly.GetTypes()) {
						var resolvesAttributes = type.GetCustomAttributes (typeof(ResolvesAttribute), false).Cast<ResolvesAttribute> ();
						if (resolvesAttributes.Any (a => typeToResolve.IsAssignableFrom (a.ResolvesTo))) {
							resolvedType = type;
							break;
						}
					}
				} catch (ReflectionTypeLoadException) {
				}
				if (resolvedType != null)
					break;
			}
			if (resolvedType == null) {
				resolvedObject = null;
				return false;
			}

			resolvedObject = Register (typeToResolve, resolvedType);
			return true;
		}

		private sealed class RegisteredObject
		{
			private readonly Lazy<object> _concreteValue;

			public RegisteredObject (Func<object> func, Type objectType)
			{
				ObjectType = objectType;
				_concreteValue = new Lazy<object> (func);
			}

			public object Instance {
				get { return _concreteValue.Value; }
			}

			public Type ObjectType { get; private set; }
		}
	}

	/// <summary>
	/// Indicates that a class can be used to resolve a type.
	/// <remarks>Using <see cref="Tiko.TikoContainer.Register"/> to register a type takes priority; this attribute is used as a fallback.</remarks>
	/// <example>
	/// <code>
	/// interface IStorageContainer {
	/// 	void SaveToStorage(string data);
	/// }
	/// 
	/// [Resolves(typeof(IStorageContainer))]
	/// class FileStorage : IStorageContainer {
	/// 	void SaveToStorage(string data) {
	/// 		using(FileStream stream = new FileStream("somefile.txt")
	/// 			stream.Write(data);
	/// 	}
	/// }
	/// 
	/// // TikoContainer.Resolve<IStorageContainer>() will now return an instance of FileStorage
	/// </code>
	/// </example>
	/// </summary>
	[AttributeUsage (AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
	public sealed class ResolvesAttribute : Attribute
	{
		public Type ResolvesTo;

		public ResolvesAttribute (Type to)
		{
			ResolvesTo = to;
		}
	}

	/// <summary>
	/// Indicates that a class or assembly depends on an external assembly
	/// <remarks>This should be used when using <see cref="Tiko.TikoContainer.Resolve"/> fails to find a type resolved with <see cref="ResolvesAttribute"/>.</remarks>
	/// </summary>
	[AttributeUsage (AttributeTargets.Assembly | AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
	public sealed class UsesAttribute : Attribute
	{
		public Assembly Assembly;

		/// <summary>
		/// Indicates that the target depends on the given assembly.
		/// </summary>
		/// <param name="assembly">The dependent assembly.</param>
		/// <seealso cref="UsesAttribute(Type)"/>
		public UsesAttribute (Assembly assembly)
		{
			Assembly = assembly;
		}

		/// <summary>
		/// Indicates that the target depends on the assembly of the given type.
		/// </summary>
		/// <param name="typeFromAssembly">A type from the dependent assembly.</param>
		/// <example>
		/// <code>
		/// [Uses(typeof(Implementation.SomeClass))]
		/// class Consumer {
		/// 	// ...
		/// }
		/// </code>
		/// </example>
		public UsesAttribute (Type typeFromAssembly) : this(typeFromAssembly.Assembly)
		{
		}
	}

	[AttributeUsage (AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
	public sealed class DependencyAttribute : Attribute
	{
	}

	[Serializable]
	public sealed class DependencyMissingException : Exception
	{
		public DependencyMissingException ()
		{
		}

		public DependencyMissingException (string message) : base(message)
		{
		}

		public DependencyMissingException (string message, Exception inner) : base(message, inner)
		{
		}
	}
}