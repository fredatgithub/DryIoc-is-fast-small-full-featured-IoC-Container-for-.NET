/*md
<!--Auto-generated from .cs file, the edits here will be lost! -->

# Rules and Default Conventions

[TOC]

## General Approach

- DryIoc strives to be as deterministic as possible. By default in uncertain case it will rather throw exception than hinder the case with convention - because "never fail silently" and "be smart but not smarter than you". 

    __Note:__ Leading towards the Best Practices and Pit-Of-Success implemented by providing more simple, less noisy, configured by default API, rather than feature prohibition.

- DryIoc designed to be unobtrusive, with ability to start using DI with legacy code and 3rd party libraries. It has small footprint and no particular requirements for consumer code.

- DryIoc provides sensible defaults but allows to override them. Based on defaults nature they may be overridden per Container(s) with Rules or per Service with optional parameters and Setup object. Rules per Container(__s__) mean that Rules object may be created once for your typical needs and then reused in many containers.

## Resolution order

DryIoc follows certain order when looking for service to resolve:

1. Look for concrete type registration. For generic service it means looking for closed-generic registration.
2. If not found and service is generic: Look for open-generic service registration.
3. If not found: Look for [Wrapper](Wrappers) of requested type.
4. If not found: Resolve from Fallback Containers if any.
5. If not resolved: Resolve with Unknown Service Resolver rules if any.

Additionally after service is found Container will look for any matching [Decorators](Decorators).


## Multiple services

### Registering multiple default services

- DryIoc allows to register multiple default (without key) services. Actually multiple services mean multiple implementations of single service type:

        container.Register<ICommand, Copy>();
        container.Register<ICommand, Paste>();

    This default behavior specified with default value of _ifAlreadyRegistered_ optional parameter in Register method. The equivalent to the previous code would be: 

        container.Register<ICommand, Copy>(ifAlreadyRegistered: IfAlreadyRegistered.AppendNonKeyed);
        container.Register<ICommand, Paste>(ifAlreadyRegistered: IfAlreadyRegistered.AppendNonKeyed); 

Other options are Throw, Keep, Replace:

- `Throw` will throw exception on second registration for the same service type.
- `Keep` will preserve all previous registrations for the service type, basically it turns method Register to Register Once.
- `Replace` will replace __all__ previous service registrations with the new one. 

__Note:__ The result of `Replace` may be not what you expect if first service was already resolved and cached by Container. 
To enable replace after resolve you should __specifically register for it__:

    - Registering with `asResolutionCall` setup makes consumers depend on call to `Resolve` instead of inline service creation which is not replaceable:

            container.Register<ICommand, Copy>(
                setup: Setup.With(asResolutionCall: true));
            container.Register<Menu>(); // depends on commands
            container.Resolve<Menu>(); // gets Copy
        
            container.Register<ICommand, FastCopy>(ifAlreadyRegistered: IfAlreadyRegistered.Replace);
            container.Resolve<Menu>(); // gets FastCopy

- `AppendNewImplementation`:

    - will add new Implementation type (__if specified__) for the service type, 
    - will skip the registration if the Implementation type is already registered for this service type,
    - will add registration without Implementation type, e.g. from `RegisterDelegate` or register with `Made.Of`. 


### Resolving from multiple default services

- DryIoc does not use any smart policy to select from multiple defaults. Sometimes multiple registrations are due error, sometimes you want to get latest. But container most be defensive, deterministic and fail fast. Therefore when resolving single service from multiple available it will throw `ContainerException` with message "Unable to select from multiple default <registration list> ...".

- You may change the Rules for service selection per Container. For instance to select latest registration:
```
#!c#
    var container = new Container(rules => rules
        .WithFactorySelector(Rules.SelectLastRegisteredFactory));
    
    container.Register<ILetter, A>();
    container.Register<ILetter, B>();
    
    var letter = container.Resolve<ILetter>(); // will return B without exception
```


## Injecting dependency asResolutionCall

By default DryIoc is injecting dependency by directly putting dependency creation expression into dependency holder constructor 
(or property, field, factory method).

Like this:
```
#!c#
    public class Dep : IDep {}

    public class Holder 
    {
        public Holder(IDep dep) {}
    }


    var container = new Container();
    
    container.Register<IDep, Dep>();
    container.Register<Holder>();
    
    var holder = container.Resolve<Holder>();

    // To make resolve possible, DryIoc constructs and then calls the following delegate (pseudocode):
    // resolver => new Holder(new Dep());
```

Pretty straightforward. It will be more complecated for reused dependency, but the `new Dep()` still be there, embedded into holder expression. 

Such approach also exposes the reason why is the recursive creation of `Dep` is not possible, the expression would've been infinite: 

    new Dep(new Dep(new Dep.....to infinity 

But sometimes the recursion, or generally the dynamic resolution of dependency is required. 
For instance when injecting [Lazy](Wrappers#markdown-header-lazy-of-a) we can expect the recursion to be possible.

Given approach with embedded expression, it is not possible:

    public class Holder 
    {
        public Holder(Lazy<IDep> dep) {}
    }

    // resolver => new Holder(new Lazy(() => new Dep()))

Therefore DryIoc supports the special factory setup option `asResolutionCall`. 
`Lazy` wrapper has this option enabled by default. 

To enable it to for normal dependency you need to change the `Dep` registration:
```
#!c#
    var container = new Container();
    
    container.Register<IDep, Dep>(setup: Setup.With(asResolutionCall: true));
    container.Register<Holder>();
    
    var holder = container.Resolve<Holder>();
    
    // will produce two delegates:
    // 1. resolver => new Holder(resolver.Resolve<IDep>())

    // The second will be created on resolver.Resolve<IDep>() call:
    // 2. resolver => new Dep()
```

Instead of inlined expression DryIoc will embed the nested call to `Resolve` method, which black boxes the dependency creation for its holder.
Such approach will make the recursion generally possible for `Lazy` and `Func` wrappers.


## Implicit registration selection based on scope

By default Container will implicitly skip the services that do not have matching __Current__ or __Resolution__ scopes.

Let's illustrate this with example:
```
#!c#
    var container = new Container();
    container.Register<I, A>(Reuse.InCurrentScope("a"));
    container.Register<I, B>(Reuse.InCurrentScope("b"));

    using (var scopeB = container.OpenScope("b")) 
    {
        var b = scope.Resolve<I>(); // will skip A registration because open scope has different name.
        Assert.IsInstanceOf<B>(b);
    }
```

__Note:__ This way you may identify services based on their reuse rather than using explicit service keys.

The rule could be turned Off:
```
#!c#
    var container = new Container(rules =>
        rules.WithoutImplicitCheckForReuseMatchingScope());

    container.Register<I, A>(Reuse.InCurrentScope("a"));
    container.Register<I, B>(Reuse.InCurrentScope("b"));

    using (var scopeB = container.OpenScope("b")) 
        scope.Resolve<I>(); // Throws exception because of multiple I registrations available
```


## Implicitly available services

DryIoc automatically (without registration) will resolve: `IResolver`, `IRegistrator`, `IContainer`, 
and `IDisposable`. 

- The first three are interfaces implemented by `Container` class and provide access to corresponding container roles.
- The `IDisposable` provides access to current [Resolution Scope](ReuseAndScopes#markdown-header-reuseinresolutionscope).

### Container interfaces

Normally using container directly in your services indicates a smell and referred as [Service Locator anti-pattern](http://blog.ploeh.dk/2010/02/03/ServiceLocatorisanAnti-Pattern/).

There are still number of cases when it may be useful. For instance, integration with other libraries.

DryIoc made these interfaces automatically available because registering them manually may be tricky, especially 
in presence of Open Scope.
```
#!c#
    class X 
    { 
        public readonly IResolver Resolver;
        public X(IResolver resolver) { Resolver = resolver; } 
    }

    var container = new Container();
    container.Register<X>(Reuse.InCurrenScope);

    using (var scope = container.OpenScope()) 
    {
        var x = scope.Resolve<X>();
        
        // the expected behavior is that x.Resolver will be the one from scope, not from container
        Assert.AreSame(scope, x.Resolver); // green!
    }
```

Given the example you can see that registering `container` object will not get you `scope` for scoped services.

The right way to register container interfaces manually __with correct scoping behavior__:
```
#!c#
    container.RegisterDelegate<IResolver>(resolver => resolver, setup: setup.Setup(allowTransientDisposable: true));
    container.RegisterDelegate<IRegistrator>(resolver => (IRegistrator)resolver, setup: setup.Setup(allowTransientDisposable: true));
    container.RegisterDelegate<IContainer>(resolver => (IContainer)resolver setup: setup.Setup(allowTransientDisposable: true));
```

The above registrations look rather complex and __leaky with all this casting in place__.

There is much easy and error-prone for User to make these registrations always available. 

__Note:__ In case you have your own implementation of aforementioned interfaces, you may register them normally,
and they override the implicit behavior. It is possible because internally these interface registered as [Wrappers](Wrappers).
Wrapper resolution has a lower priority comparing to normal Service resolution and will be resolved as fallback.


### IDisposable

Injected `IDisposable` object provides access to current [Resolution Scope](ReuseAndScopes#markdown-header-reuseinresolutionscope) 
and allows to dispose the scope together with reused services. 

```
#!c#
    container.Register<A>(setup: Setup.With(openResolutionScope: true));
    container.Register<B>();
    container.Register<C>(reuse: Reuse.InResolutionScope);

    public class B { public B(C c) { } }

    public class A : IDisposable 
    {
        public A(B b, IDisposable scope) { }
        public void Dispose() 
        {
            // Will dispose scope with all nested scoped dependencies 
            // including C used by B
            _scope.Dispose(); 
        } 
    }
```

__Note:__ Usage of `IDisposable` here is similar to [Autofac Owned relationship type](http://docs.autofac.org/en/latest/resolve/relationships.html#controlled-lifetime-owned-b), but it does not require your code to depend on IoC library.

To inject any other service as `IDisposable` you may specify [Required Service Type](RequiredServiceType).

```
#!c#
    container.Register<A>();
    container.Register<Disposer>(Made.Of(() => new Disposer(Arg.Of<A>())));

    public class Disposer { public Disposer(IDisposable thing) { } }
```

## Default constructor selection

By default DryIoc expects from registered type to have __single public constructor__. 

If no or multiple constructors available it will throw corresponding exception. The reason for this is to be as deterministic as possible and prevent hard-to-find errors.

But the default behavior may be changed in different ways:

- You may specify predefined `FactoryMethod.ConstructorWithResolvableArguments` rule to be used for registration or per Container. The rule will work if multiple constructors available, and will select the constructor with maximum number of parameters where each parameter is successfully resolved from container.
```
#!c#
        // Enabling the rule per container:
        var container = new Container(rules => rules.With(
            FactoryMethod.ConstructorWithResolvableArguments));

        // Enabling the rule per registration:
        container.Register<A>(made: Made.Of(
            FactoryMethod.ConstructorWithResolvableArguments));
```

- You may specify to use specific constructor, or even static or instance method, field or property for producing the service. The preferred way to do it is using `Made.Of` expression specification:

        container.Register<A>(Made.Of(() => new A(Arg.Of<B>(), Arh.Of<C>("someKey"))));

    [More examples are here](SelectConstructorOrFactoryMethod).

## Unresolved parameters and properties

By default DryIoc will throw exception if constructor or method parameter is not resolved, __but will not throw in case of property or field, or in case of optional parameter__ (yes, DryIoc supports optional parameters with default values just fine).

This is because "normally" constructor parameters specify required dependencies. On the other hand writable properties usually specify dependencies that may be not set when object is created, set by third-party, or not set at all.

As usual, you may override default behavior to throw an exception for unresolved property, or set the default value for unresolved parameter:
```
#!c#
    public class A
    {
        public P P { get; set; }
        public A(B b, C c = null) {}
    }

    // Override unresolved behavior:
    container.Register<A>(Made.Of(() => 
        new A(
            Arg.Of<B>(IfUnresolved.ReturnDefault),  // return default even for parameter
            Arg.Of<C>(IfUnresolved.Throw))          // throw for optional parameter
            { 
                P = Arg.Of<P>(IfUnresolved.Throw)   // throw for property
            }));
```


## Rules per Container

### FactorySelector

__Note:__ In DryIoc _Factory_ is the unit of registration. Speaking of factory selection we are speaking of registration selection.

Allows to override default factory selection, especially when we have multiple registered default factories.

DryIoc has two predefined rules that you can use instead of [default policy](RulesAndDefaultConventions#markdown-header-resolving-from-multiple-default-services):

- `Rules.SelectLastRegisteredFactory` - explained [here](RulesAndDefaultConventions#markdown-header-resolving-from-multiple-default-services).
- `Rules.SelectKeyedOverDefaultFactory(serviceKey)` - allows to prefer registration with specific key over the default. 
    
    For example you may register some dependencies to be available only inside opened scope:

        var container = new Container();
        container.Register<I, A>();
        container.Register<I, B>(serviceKey: "scoped");

        using (var scope = container.OpenScope(configure: rules => rules
            .WithFactorySelector(Rules.SelectKeyedOverDefaultFactory("scoped")))) 
        {
            // will return B instead of A despite the absence of "scoped" key in resolve
            container.Resolve<I>(); 
        }


### FactoryMethod, Parameters and Properties selector

This way you may specify how to select constructor, parameters and properties. 

For instance [MefAttributedModel](Extensions/MefAttributedModel) uses these rules to instruct DryIoc to select constructor and properties marked with `ImportingConstructor` and `Import` attributes.

Also you can use the rule to [select constructor with all resolvable parameters](RulesAndDefaultConventions#markdown-header-default-constructor-selection).


### UnknownServiceResolvers

The rules is used as a last resort / fallback resolution strategy when no registration is found.

You may use this rule to implement on-demand registrations, or automatic concrete types registrations, etc.

#### WithAutoFallbackResolution

DryIoc provides predefined rule `AutoRegisterUnknownServiceRule` to register missing service from provided list of types or assemblies:

    var container = new Container(rules =>
        rules.WithUnknownServiceResolvers(Rules.AutoRegisterUnknownServiceRule(implTypes));

To simplify this scenario DryIoc defines extension method:

    container = container.WithAutoFallbackResolution(implTypes);
    // or
    container = container.WithAutoFallbackResolution(assemblies);

- In addition you can specify to `changeDefaultReuse` that by default is Singleton or InCurrentScope (if missing service requested in a scope).
- You may also pass `condition` to select only some of services based on context.


#### WithAutoConcreteTypeResolution

The rule (and corresponding sugar extension method) to automatically resolve concrete (non-interface, non-abstract) types without registering them in container.

Using the rule:

    var container = new Container(rules =>
        rules.WithUnknownServiceResolvers(Rules.AutoResolveConcreteTypeRule(optionalCondition));

Using the extension method:

    class Driver {}
    class FastCar : ICar 
    {
        public FastCar(Driver driver) {}
    } 

    var container = new Container().WithAutoConcreteTypeResolution();

    container.Register<ICar, FastCar>();
    // no Driver registration!

    // Driver is created by container
    var car = container.Resolve<ICar>();


### Fallback Containers

[Explained in "Child" containers](KindsOfChildContainer#markdown-header-facade).


### ThrowIfDependencyHasShorterReuseLifespan

__This rule is enabled by default__ and instructs container to throw exception when injecting dependency with shorter lifespan than dependency holder. 

What does it mean?

- In DryIoc services with Singleton reuse have a longest lifespan equal to lifespan of container itself.
- Then go services with Reuse.InCurrentScope which live no longer than singletons.
- Transient services do not have a lifespan, so the rule is not applied for them.
- Services reused in ResolutionScope are similar to transients in a way that container does not hold on them, 
so their lifespan is not comparable with singletons and current scope. The rule is not applied for them too.

From implementation point of view `Lifespan` is the property defined in `IReuse` interface, and the respective implementations define relative lifespan values for the property:

- `SingletonReuse.Lifespan` is `1000`
- `CurrentScopeReuse.Lifespan` is `100`
- `ResolutionScope.Lifespan` is `0`

When defining your own Reuse you may take advantage of the rule by defining specific Lifespan number.

Example:
```
#!c#
    class A { public A(B b) {} }

    var container = new Container(); // enabled by default

    container.Register<A>(Reuse.Singleton);
    container.Register<B>(Reuse.InCurrentScope);

    using (var scope = container.OpenScope())
        scope.Resolve<A>(); // Throws container exception with explanation and guidance
```

You may disable the rule for Container, so lifespan mismatch will not throw exception.
```
#!c#
    var container = new Container(rules => rules
        .WithoutThrowIfDependencyHasShorterReuseLifespan());
    
    // ... the same setup
    using (var scope1 = container.OpenScope())
        _a = scope1.Resolve<A>(); // OK

    using (var scope2 = container.OpenScope())
        _a = scope2.Resolve<A>(); // OK, but _a still holds B from scope1
```

__Note:__ Another way to skip the lifespan diagnostics is to wrap dependency in `Func`. 
Using `Func` means that client is in charge of creating dependency whenever its needed, so the check does not make sense.

```
#!c#
    class A { public A(Func<B> getB) {} }

    var container = new Container();

    container.Register<A>(Reuse.Singleton);
    container.Register<B>(Reuse.InCurrentScope);

    container.Resolve<A>(); // works, A will decide when to create B.
```


### ThrowOnRegisteringDisposableTransient

DryIoc does not track disposable transients by default as described [here](ReuseAndScopes#markdown-header-disposable-transient).

That means you may register Transient `IDisposable` and forgot to dispose it, thinking that Container will do this for you.

To prevent possible memory leaks due ignored `Dispose`, DryIoc by default will throw exception on registering transient implementing `IDisposable` interface:

    container.Register<MyDisposableService>(); // Throws exception!
    container.Register<MyDisposableService>(Reuse.InResolutionScope); // OK

If you disagree, you may silence this exception per registration or (more care-free) per Container:
```
#!c#
    // per registration:
    container.Register<MyDisposableService>(
        setup: Setup.With(allowDisposableTransient: true));

    // per container:
    var container = new Container(rules =>
        rules.WithoutThrowOnRegisteringDisposableTransient);
    container.Register<MyDisposableService>();
```

### WithTrackingDisposableTransient

In detail described [here](ReuseAndScopes#markdown-header-disposabletransient).


### WithDefaultReuseInsteadOfTransient

Allows to specify different default Reuse per Container as described [here in Reuse and Scopes](ReuseAndScopes#markdown-header-different-default-reuse-instead-of-transient).


### WithDefaultIfAlreadyRegistered

Allows to specify registration option per Container which is different from default `IfAlreadyRegistered.AppendNonKeyed`. 

For instance I want my container to follow _Register Once_ registration semantics:
```
#!c#
    var container = new Container(rules => rules
        .WithDefaultIfAlreadyRegistered(IfAlreadyRegistered.Keep));
    
    container.Register<I, A>();
    container.Register<I, B>();
    
    var i = container.Resolve<I>();
    
    Assert.IsInstanceOf<A>(i); // the first registration will be kept, and the second ignored
```

Another interesting use is to make __Collection Registration Explicit__. Given previous example I need to explicitly specify `IfAlreadyRegistered.AppendNonKeyed` for individual registration to be added to collection:
```
#!c#
    var container = new Container(rules => rules
        .WithDefaultIfAlreadyRegistered(IfAlreadyRegistered.Keep));
    
    container.Register<I, A>(ifAlreadyRegistered: IfAlreadyRegistered.AppendNonKeyed);
    container.Register<I, B>(ifAlreadyRegistered: IfAlreadyRegistered.AppendNonKeyed);
    
    var ii = container.Resolve<IEnumerable<I>>();
    Assert.AreEqual(2, ii.Count());
```


### WithoutImplicitCheckForReuseMatchingScope 

This rule turns Off the default [Implicit registration selection based on scope](RulesAndDefaultConventions#markdown-header-implicit-registration-selection-based-on-scope).


### ResolveIEnumerableAsLazyEnumerable

[Explained in Wrappers](Wrappers#markdown-header-lazyenumerable-of-a).


### VariantGenericTypesInResolvedCollection

[Explained in Wrappers](Wrappers#markdown-header-contravariant-generics).


### WithThrowIfRuntimeStateRequired

Specifies to throw an exception in attempt to resolve service which require runtime state for resolution.
Runtime state may be introduced by `RegisterDelegate`, `RegisterInstance`, or registering with non-primitive service key, or metadata.

This rule is helpful for compile-time generation of resolution delegates, e.g. by __DryIocZero__.
The problem with the stateful registrations (delegates, services instances, key or metadata objects) 
that there is no way to make them available at compile-time.

`WithThrowIfRuntimeStateRequired` rule Container will report presence of run-time state via exception, 
so that compile-time generation process may notify user about problematic registrations.
```
#!c#
    var container = new Container(rules => rules.WithThrowIfRuntimeStateRequired());

    container.RegisterDelegate<string>(_ => "a");

    container.Resolve<string>(); // throws exception
```

__Note:__ __Runtime state should be avoided generally__, because it is like a black box for container, 
which disables number of diagnostics available for normal Type based registrations. 
Especially two potential error checks: 

- Reuse Lifespan mismatch
- Recursive dependency detection

md*/
