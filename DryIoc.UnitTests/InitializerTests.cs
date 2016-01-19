﻿using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace DryIoc.UnitTests
{
    [TestFixture]
    public class InitializerTests
    {
        [Test]
        public void Can_register_initializer_as_decorator_delegate()
        {
            var container = new Container();
            container.Register<InitializableService>();
            container.RegisterDelegate<Func<InitializableService, InitializableService>>(r => x =>
            {
                x.Initialize("blah");
                return x;
            }, setup: Setup.Decorator);

            var service = container.Resolve<InitializableService>();

            Assert.That(service.Data, Is.EqualTo("blah"));
        }

        [Test]
        public void Can_register_initializer_as_decorator_delegate_of_generic_impl()
        {
            var container = new Container();
            container.Register<IInitializable<InitializableService>, InitializableService>();
            container.RegisterDelegate<Func<IInitializable<InitializableService>, IInitializable<InitializableService>>>(
                r => x => x.Initialize("blah"), 
                setup: Setup.Decorator);

            var service = (InitializableService)container.Resolve<IInitializable<InitializableService>>();

            Assert.That(service.Data, Is.EqualTo("blah"));
        }

        [Test]
        public void Can_register_initializer_as_decorator_delegate_with_dedicated_method()
        {
            var container = new Container();
            container.Register<InitializableService>();
            container.RegisterInitializer<IInitializable>((x, _) => x.Initialize("yeah"));

            var service = container.Resolve<InitializableService>();

            Assert.That(service.Data, Is.EqualTo("yeah"));
        }

        [Test]
        public void Can_register_and_inject_initializer_as_decorator_delegate_with_dedicated_method()
        {
            var container = new Container();
            container.Register<ClientOfInitializableService>();
            container.Register<InitializableService>();
            container.RegisterInitializer<IInitializable>((x, _) => x.Initialize("yeah"));

            var client = container.Resolve<ClientOfInitializableService>();

            Assert.That(client.Service.Data, Is.EqualTo("yeah"));
        }

        [Test]
        public void Can_chain_initializers_as_decorator_delegate_with_dedicated_method()
        {
            var container = new Container();
            container.Register<ClientOfInitializableService>();
            container.Register<InitializableService>();
            container.RegisterInitializer<IInitializable>((x, _) => x.Initialize("yeah"));
            container.RegisterInitializer<IInitializable<InitializableService>>((x, _) => x.Initialize("blah"));

            var client = container.Resolve<ClientOfInitializableService>();

            Assert.That(client.Service.Data, Is.StringContaining("yeah"));
            Assert.That(client.Service.Data, Is.StringContaining("blah"));
        }

        [Test]
        public void Can_register_and_call_one_initializer_multiple_times_with_different_parameters()
        {
            var container = new Container();
            container.Register<ClientOfInitializableService>();
            container.Register<InitializableService>();
            container.RegisterInitializer<IInitializable>((x, _) => x.Initialize("green"));
            container.RegisterInitializer<IInitializable>((x, _) => x.Initialize("-blah"));

            var client = container.Resolve<ClientOfInitializableService>();

            Assert.That(client.Service.Data, Is.EqualTo("green-blah"));
        }

        [Test]
        public void Can_register_initializer_for_object_For_example_to_log_all_resolutions()
        {
            var container = new Container();
            container.Register<InitializableService>();

            var log = new List<string>();
            container.RegisterInitializer<object>((x, _) => log.Add(x.GetType().Name));

            container.Resolve<InitializableService>();

            CollectionAssert.AreEqual(new[] { "InitializableService" }, log);
        }

        [Test]
        public void Can_track_disposable_transient_in_scope_via_initializer()
        {
            var container = new Container(r => r.WithoutThrowOnRegisteringDisposableTransient());
            RegisterDisposableTransientsTracker(container, Reuse.InCurrentScope);

            container.Register<ADisp>(Reuse.Transient);

            var scope = container.OpenScope();
            var a = scope.Resolve<ADisp>();
            scope.Dispose();

            Assert.IsTrue(a.IsDisposed);
        }

        [Test]
        public void Can_track_injected_disposable_transient_in_scope_via_initializer()
        {
            var container = new Container(r => r.WithoutThrowOnRegisteringDisposableTransient());
            RegisterDisposableTransientsTracker(container, Reuse.InCurrentScope);

            container.Register<ADisp>();
            container.Register<B>();

            var scope = container.OpenScope();
            var b = scope.Resolve<B>();
            scope.Dispose();

            Assert.AreNotSame(b.A1, b.A2);
            Assert.IsTrue(b.A1.IsDisposed);
            Assert.IsTrue(b.A2.IsDisposed);
        }

        [Test, Ignore]
        public void Can_track_disposable_transient_in_scope_and_singleton_via_initializer()
        {
            var container = new Container(r => r.WithoutThrowOnRegisteringDisposableTransient());
            RegisterDisposableTransientsTracker(container, Reuse.Singleton, Reuse.InCurrentScope);

            container.Register<ADisp>(Reuse.Transient);

            var a = container.Resolve<ADisp>();

            var scope = container.OpenScope();
            var b = scope.Resolve<ADisp>();
            scope.Dispose();

            Assert.IsTrue(b.IsDisposed);
            Assert.IsFalse(a.IsDisposed);

            container.Dispose();
            Assert.IsTrue(a.IsDisposed);
        }

        public class B
        {
            public ADisp A1 { get; private set; }

            public ADisp A2 { get; private set; }

            public B(ADisp a1, ADisp a2)
            {
                A1 = a1;
                A2 = a2;
            }
        }

        public class ADisp : IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }

        private static void RegisterDisposableTransientsTracker(IRegistrator container, params IReuse[] reuses)
        {
            for (var i = 0; i < reuses.Length; i++)
                container.Register<TransientDisposablesTracker>(reuses[i].ThrowIfNull());

            container.RegisterInitializer<object>((t, r) =>
            {
                var disposable = t as IDisposable;
                if (disposable != null)
                    r.Resolve<TransientDisposablesTracker>().Track(disposable);
            },
            request => request.ReuseLifespan == 0 /* transient */);
        }

        public class TransientDisposablesTracker : IDisposable
        {
            private readonly Ref<IDisposable[]> _items = Ref.Of(ArrayTools.Empty<IDisposable>());

            public void Track(IDisposable disposable)
            {
                _items.Swap(i => i.AppendOrUpdate(disposable));
            }

            public void Dispose()
            {
                var items = _items.Value;
                for (var i = 0; i < items.Length; i++)
                    items[i].Dispose();
                _items.Swap(_ => ArrayTools.Empty<IDisposable>());
            }
        }

        public interface IInitializable<T>
        {
            T Initialize(string data);
        }

        public interface IInitializable
        {
            void Initialize(string data);
        }

        public class InitializableService : IInitializable<InitializableService>, IInitializable
        {
            public string Data = String.Empty;

            public InitializableService Initialize(string data)
            {
                Data += data;
                return this;
            }

            void IInitializable.Initialize(string data)
            {
                Data += data;
            }
        }

        public class ClientOfInitializableService
        {
            public InitializableService Service { get; private set; }

            public ClientOfInitializableService(InitializableService service)
            {
                Service = service;
            }
        }
    }
}
