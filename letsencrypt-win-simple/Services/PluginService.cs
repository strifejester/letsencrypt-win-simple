﻿using Autofac;
using LetsEncrypt.ACME.Simple.Plugins.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LetsEncrypt.ACME.Simple.Services
{
    public class PluginService
    {
        private readonly List<Type> _targetFactories;
        private readonly List<Type> _validationFactories;
        private readonly List<Type> _storeFactories;
        private readonly List<Type> _installationFactories;
        private readonly List<Type> _target;
        private readonly List<Type> _validation;
        private readonly List<Type> _store;
        private readonly List<Type> _installation;

        public List<ITargetPluginFactory> TargetPluginFactories(ILifetimeScope scope)
        {
            return GetFactories<ITargetPluginFactory>(_targetFactories, scope);
        }

        public List<IValidationPluginFactory> ValidationPluginFactories(ILifetimeScope scope)
        {
            return GetFactories<IValidationPluginFactory>(_validationFactories, scope);
        }

        public List<IStorePluginFactory> StorePluginFactories(ILifetimeScope scope)
        {
            return GetFactories<IStorePluginFactory>(_storeFactories, scope);
        }

        public List<IInstallationPluginFactory> InstallationPluginFactories(ILifetimeScope scope)
        {
            return GetFactories<IInstallationPluginFactory>(_installationFactories, scope);
        }

        public ITargetPluginFactory TargetPluginFactory(ILifetimeScope scope, string name)
        {
            return GetByName<ITargetPluginFactory>(_targetFactories, name, scope);
        }

        public IValidationPluginFactory ValidationPluginFactory(ILifetimeScope scope, string full)
        {
            var split = full.Split('.');
            var name = split[1];
            var type = split[0];
            return _validationFactories.
                Select(t => scope.Resolve(t)).
                OfType<IValidationPluginFactory>().
                FirstOrDefault(x => x.Match(name) && string.Equals(type, x.ChallengeType, StringComparison.InvariantCultureIgnoreCase));
        }

        public IStorePluginFactory StorePluginFactory(ILifetimeScope scope, string name)
        {
            return GetByName<IStorePluginFactory>(_storeFactories, name, scope);
        }

        public IInstallationPluginFactory InstallationPluginFactory(ILifetimeScope scope, string name)
        {
            return GetByName<IInstallationPluginFactory>(_installationFactories, name, scope);
        }

        internal void Configure(ContainerBuilder builder)
        {
            _targetFactories.ForEach(t => { builder.RegisterType(t).SingleInstance(); });
            _validationFactories.ForEach(t => { builder.RegisterType(t).SingleInstance(); });
            _storeFactories.ForEach(t => { builder.RegisterType(t).SingleInstance(); });
            _installationFactories.ForEach(t => { builder.RegisterType(t).SingleInstance();});

            _target.ForEach(ip => { builder.RegisterType(ip); });
            _validation.ForEach(ip => { builder.RegisterType(ip); });
            _store.ForEach(ip => { builder.RegisterType(ip); });
            _installation.ForEach(ip => { builder.RegisterType(ip); });
        }

        private List<T> GetFactories<T>(List<Type> source, ILifetimeScope scope) where T : IHasName, IHasType
        {
            return source.Select(t => scope.Resolve(t)).OfType<T>().ToList();
        }

        private T GetByName<T>(IEnumerable<Type> list, string name, ILifetimeScope scope) where T: IHasName
        {
            return list.Select(t => scope.Resolve(t)).OfType<T>().FirstOrDefault(x => x.Match(name));
        }

        public PluginService()
        {
            _targetFactories = GetResolvable<ITargetPluginFactory>();
            _validationFactories = GetResolvable<IValidationPluginFactory>();
            _storeFactories = GetResolvable<IStorePluginFactory>();
            _installationFactories = GetResolvable<IInstallationPluginFactory>(true);

            _target = GetResolvable<ITargetPlugin>();
            _validation = GetResolvable<IValidationPlugin>();
            _store = GetResolvable<IStorePlugin>();
            _installation = GetResolvable<IInstallationPlugin>();
        }

        private List<Type> GetResolvable<T>(bool allowNull = false)
        {
            var ret = Assembly.GetExecutingAssembly()
                        .GetTypes()
                        .Where(type => typeof(T) != type && typeof(T).IsAssignableFrom(type) && !type.IsAbstract);
            if (!allowNull)
            {
                ret = ret.Where(type => !typeof(INull).IsAssignableFrom(type));
            }
            return ret.ToList();
        }

    }
}
