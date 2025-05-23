// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Internal;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods to configure an <see cref="IServiceCollection"/> for <see cref="IHttpClientFactory"/>.
    /// </summary>
    public static class HttpClientFactoryServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/>.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <returns>The <see cref="IServiceCollection"/>.</returns>
        public static IServiceCollection AddHttpClient(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddLogging();
            services.AddOptions();
#if NET8_0_OR_GREATER
            services.AddMetrics();
#endif

            //
            // Core abstractions
            //
            services.TryAddTransient<HttpMessageHandlerBuilder, DefaultHttpMessageHandlerBuilder>();
            services.TryAddSingleton<DefaultHttpClientFactory>();
            services.TryAddSingleton<IHttpClientFactory>(serviceProvider => serviceProvider.GetRequiredService<DefaultHttpClientFactory>());
            services.TryAddSingleton<IHttpMessageHandlerFactory>(serviceProvider => serviceProvider.GetRequiredService<DefaultHttpClientFactory>());

            //
            // Typed Clients
            //
            services.TryAdd(ServiceDescriptor.Transient(typeof(ITypedHttpClientFactory<>), typeof(DefaultTypedHttpClientFactory<>)));
            services.TryAdd(ServiceDescriptor.Singleton(typeof(DefaultTypedHttpClientFactory<>.Cache), typeof(DefaultTypedHttpClientFactory<>.Cache)));

            //
            // Misc infrastructure
            //
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, LoggingHttpMessageHandlerBuilderFilter>());
#if NET8_0_OR_GREATER
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, MetricsFactoryHttpMessageHandlerFilter>());
#endif

            // This is used to track state and report errors **DURING** service registration. This has to be an instance
            // because we access it by reaching into the service collection.
            services.TryAddSingleton(new HttpClientMappingRegistry());

            // This is used to store configuration for the default builder.
            services.TryAddSingleton(new DefaultHttpClientConfigurationTracker());

            // Register default client as HttpClient
            services.TryAddTransient(s =>
            {
                return s.GetRequiredService<IHttpClientFactory>().CreateClient(string.Empty);
            });

            return services;
        }

        /// <summary>
        /// Adds a delegate that will be used to configure all <see cref="HttpClient"/> instances.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="configure">A delegate that is used to configure an <see cref="IHttpClientBuilder"/>.</param>
        /// <returns>The <see cref="IServiceCollection"/>.</returns>
        public static IServiceCollection ConfigureHttpClientDefaults(this IServiceCollection services, Action<IHttpClientBuilder> configure)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configure);

            AddHttpClient(services);

            configure(new DefaultHttpClientBuilder(services, name: null!));

            return services;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a named <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="name">The logical name of the <see cref="HttpClient"/> to configure.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// Use <see cref="Options.Options.DefaultName"/> as the name to configure the default client.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(name);

            AddHttpClient(services);

            return new DefaultHttpClientBuilder(services, name);
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a named <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="name">The logical name of the <see cref="HttpClient"/> to configure.</param>
        /// <param name="configureClient">A delegate that is used to configure an <see cref="HttpClient"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// Use <see cref="Options.Options.DefaultName"/> as the name to configure the default client.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name, Action<HttpClient> configureClient)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(configureClient);

            AddHttpClient(services);

            var builder = new DefaultHttpClientBuilder(services, name);
            builder.ConfigureHttpClient(configureClient);
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a named <see cref="HttpClient"/>.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="name">The logical name of the <see cref="HttpClient"/> to configure.</param>
        /// <param name="configureClient">A delegate that is used to configure an <see cref="HttpClient"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// Use <see cref="Options.Options.DefaultName"/> as the name to configure the default client.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient(this IServiceCollection services, string name, Action<IServiceProvider, HttpClient> configureClient)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(configureClient);

            AddHttpClient(services);

            var builder = new DefaultHttpClientBuilder(services, name);
            builder.ConfigureHttpClient(configureClient);
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient"/> type and a named <see cref="HttpClient"/>. The client name
        /// will be set to the type name of <typeparamref name="TClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
            this IServiceCollection services)
            where TClient : class
        {
            ArgumentNullException.ThrowIfNull(services);

            AddHttpClient(services);

            string name = TypeNameHelper.GetTypeDisplayName(typeof(TClient), fullName: false);
            var builder = new DefaultHttpClientBuilder(services, name);
            builder.AddTypedClientCore<TClient>(validateSingleType: true);
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient" /> type and a named <see cref="HttpClient"/>. The client name will
        /// be set to the type name of <typeparamref name="TClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <typeparam name="TImplementation">
        /// The implementation type of the typed client. The type specified will be instantiated by the
        /// <see cref="ITypedHttpClientFactory{TImplementation}"/>.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<TClient, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            this IServiceCollection services)
            where TClient : class
            where TImplementation : class, TClient
        {
            ArgumentNullException.ThrowIfNull(services);

            AddHttpClient(services);

            string name = TypeNameHelper.GetTypeDisplayName(typeof(TClient), fullName: false);
            var builder = new DefaultHttpClientBuilder(services, name);
            builder.AddTypedClientCore<TClient, TImplementation>(validateSingleType: true);
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient"/> type and a named <see cref="HttpClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="name">The logical name of the <see cref="HttpClient"/> to configure.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// <para>
        /// Use <see cref="Options.Options.DefaultName"/> as the name to configure the default client.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
            this IServiceCollection services, string name)
            where TClient : class
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(name);

            AddHttpClient(services);

            var builder = new DefaultHttpClientBuilder(services, name);
            builder.AddTypedClientCore<TClient>(validateSingleType: false); // Name was explicitly provided.
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient" /> type and a named <see cref="HttpClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <typeparam name="TImplementation">
        /// The implementation type of the typed client. The type specified will be instantiated by the
        /// <see cref="ITypedHttpClientFactory{TImplementation}"/>.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="name">The logical name of the <see cref="HttpClient"/> to configure.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// <para>
        /// Use <see cref="Options.Options.DefaultName"/> as the name to configure the default client.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<TClient, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            this IServiceCollection services, string name)
            where TClient : class
            where TImplementation : class, TClient
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(name);

            AddHttpClient(services);

            var builder = new DefaultHttpClientBuilder(services, name);
            builder.AddTypedClientCore<TClient, TImplementation>(validateSingleType: false); // name was explicitly provided
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient" /> type and a named <see cref="HttpClient"/>. The client name will
        /// be set to the type name of <typeparamref name="TClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="configureClient">A delegate that is used to configure an <see cref="HttpClient"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
            this IServiceCollection services, Action<HttpClient> configureClient)
            where TClient : class
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configureClient);

            AddHttpClient(services);

            string name = TypeNameHelper.GetTypeDisplayName(typeof(TClient), fullName: false);
            var builder = new DefaultHttpClientBuilder(services, name);
            builder.ConfigureHttpClient(configureClient);
            builder.AddTypedClientCore<TClient>(validateSingleType: true);
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient" /> type and a named <see cref="HttpClient"/>. The client name will
        /// be set to the type name of <typeparamref name="TClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="configureClient">A delegate that is used to configure an <see cref="HttpClient"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
            this IServiceCollection services, Action<IServiceProvider, HttpClient> configureClient)
            where TClient : class
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configureClient);

            AddHttpClient(services);

            string name = TypeNameHelper.GetTypeDisplayName(typeof(TClient), fullName: false);
            var builder = new DefaultHttpClientBuilder(services, name);
            builder.ConfigureHttpClient(configureClient);
            builder.AddTypedClientCore<TClient>(validateSingleType: true);
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient" /> type and a named <see cref="HttpClient"/>. The client name will
        /// be set to the type name of <typeparamref name="TClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <typeparam name="TImplementation">
        /// The implementation type of the typed client. The type specified will be instantiated by the
        /// <see cref="ITypedHttpClientFactory{TImplementation}"/>.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="configureClient">A delegate that is used to configure an <see cref="HttpClient"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<TClient, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            this IServiceCollection services, Action<HttpClient> configureClient)
            where TClient : class
            where TImplementation : class, TClient
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configureClient);

            AddHttpClient(services);

            string name = TypeNameHelper.GetTypeDisplayName(typeof(TClient), fullName: false);
            var builder = new DefaultHttpClientBuilder(services, name);
            builder.ConfigureHttpClient(configureClient);
            builder.AddTypedClientCore<TClient, TImplementation>(validateSingleType: true);
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient" /> type and a named <see cref="HttpClient"/>. The client name will
        /// be set to the type name of <typeparamref name="TClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <typeparam name="TImplementation">
        /// The implementation type of the typed client. The type specified will be instantiated by the
        /// <see cref="ITypedHttpClientFactory{TImplementation}"/>.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="configureClient">A delegate that is used to configure an <see cref="HttpClient"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<TClient, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            this IServiceCollection services, Action<IServiceProvider, HttpClient> configureClient)
            where TClient : class
            where TImplementation : class, TClient
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configureClient);

            AddHttpClient(services);

            string name = TypeNameHelper.GetTypeDisplayName(typeof(TClient), fullName: false);
            var builder = new DefaultHttpClientBuilder(services, name);
            builder.ConfigureHttpClient(configureClient);
            builder.AddTypedClientCore<TClient, TImplementation>(validateSingleType: true);
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient" /> type and a named <see cref="HttpClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="name">The logical name of the <see cref="HttpClient"/> to configure.</param>
        /// <param name="configureClient">A delegate that is used to configure an <see cref="HttpClient"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// <para>
        /// Use <see cref="Options.Options.DefaultName"/> as the name to configure the default client.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
            this IServiceCollection services, string name, Action<HttpClient> configureClient)
            where TClient : class
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(configureClient);

            AddHttpClient(services);

            var builder = new DefaultHttpClientBuilder(services, name);
            builder.ConfigureHttpClient(configureClient);
            builder.AddTypedClientCore<TClient>(validateSingleType: false); // name was explicitly provided
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient" /> type and a named <see cref="HttpClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="name">The logical name of the <see cref="HttpClient"/> to configure.</param>
        /// <param name="configureClient">A delegate that is used to configure an <see cref="HttpClient"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// <para>
        /// Use <see cref="Options.Options.DefaultName"/> as the name to configure the default client.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TClient>(
            this IServiceCollection services, string name, Action<IServiceProvider, HttpClient> configureClient)
            where TClient : class
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(configureClient);

            AddHttpClient(services);

            var builder = new DefaultHttpClientBuilder(services, name);
            builder.ConfigureHttpClient(configureClient);
            builder.AddTypedClientCore<TClient>(validateSingleType: false); // name was explicitly provided
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient" /> type and a named <see cref="HttpClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <typeparam name="TImplementation">
        /// The implementation type of the typed client. The type specified will be instantiated by the
        /// <see cref="ITypedHttpClientFactory{TImplementation}"/>.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="name">The logical name of the <see cref="HttpClient"/> to configure.</param>
        /// <param name="configureClient">A delegate that is used to configure an <see cref="HttpClient"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// <para>
        /// Use <see cref="Options.Options.DefaultName"/> as the name to configure the default client.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<TClient, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            this IServiceCollection services, string name, Action<HttpClient> configureClient)
            where TClient : class
            where TImplementation : class, TClient
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(configureClient);

            AddHttpClient(services);

            var builder = new DefaultHttpClientBuilder(services, name);
            builder.ConfigureHttpClient(configureClient);
            builder.AddTypedClientCore<TClient, TImplementation>(validateSingleType: false); // name was explicitly provided
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient" /> type and a named <see cref="HttpClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <typeparam name="TImplementation">
        /// The implementation type of the typed client. The type specified will be instantiated by the
        /// <see cref="ITypedHttpClientFactory{TImplementation}"/>.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="name">The logical name of the <see cref="HttpClient"/> to configure.</param>
        /// <param name="configureClient">A delegate that is used to configure an <see cref="HttpClient"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// <para>
        /// Use <see cref="Options.Options.DefaultName"/> as the name to configure the default client.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<TClient, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(
            this IServiceCollection services, string name, Action<IServiceProvider, HttpClient> configureClient)
            where TClient : class
            where TImplementation : class, TClient
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(configureClient);

            AddHttpClient(services);

            var builder = new DefaultHttpClientBuilder(services, name);
            builder.ConfigureHttpClient(configureClient);
            builder.AddTypedClientCore<TClient, TImplementation>(validateSingleType: false); // name was explicitly provided
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient" /> type and a named <see cref="HttpClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <typeparam name="TImplementation">
        /// The implementation type of the typed client.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="factory">A delegate that is used to create an instance of <typeparamref name="TClient"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<TClient, TImplementation>(this IServiceCollection services, Func<HttpClient, TImplementation> factory)
            where TClient : class
            where TImplementation : class, TClient
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(factory);

            string name = TypeNameHelper.GetTypeDisplayName(typeof(TClient), fullName: false);
            return AddHttpClient<TClient, TImplementation>(services, name, factory);
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient" /> type and a named <see cref="HttpClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <typeparam name="TImplementation">
        /// The implementation type of the typed client.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="name">The logical name of the <see cref="HttpClient"/> to configure.</param>
        /// <param name="factory">A delegate that is used to create an instance of <typeparamref name="TClient"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// <typeparamref name="TImplementation">
        /// </typeparamref>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<TClient, TImplementation>(this IServiceCollection services, string name, Func<HttpClient, TImplementation> factory)
            where TClient : class
            where TImplementation : class, TClient
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(factory);

            AddHttpClient(services);

            var builder = new DefaultHttpClientBuilder(services, name);
            builder.AddTypedClient<TClient>(factory);
            return builder;
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient" /> type and a named <see cref="HttpClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <typeparam name="TImplementation">
        /// The implementation type of the typed client.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="factory">A delegate that is used to create an instance of <typeparamref name="TClient"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<TClient, TImplementation>(this IServiceCollection services, Func<HttpClient, IServiceProvider, TImplementation> factory)
            where TClient : class
            where TImplementation : class, TClient
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(factory);

            string name = TypeNameHelper.GetTypeDisplayName(typeof(TClient), fullName: false);
            return AddHttpClient<TClient, TImplementation>(services, name, factory);
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient" /> type and a named <see cref="HttpClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type specified will be registered in the service collection as
        /// a transient service. See <see cref="ITypedHttpClientFactory{TClient}" /> for more details about authoring typed clients.
        /// </typeparam>
        /// <typeparam name="TImplementation">
        /// The implementation type of the typed client.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="name">The logical name of the <see cref="HttpClient"/> to configure.</param>
        /// <param name="factory">A delegate that is used to create an instance of <typeparamref name="TClient"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddHttpClient<TClient, TImplementation>(this IServiceCollection services, string name, Func<HttpClient, IServiceProvider, TImplementation> factory)
            where TClient : class
            where TImplementation : class, TClient
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(name);
            ArgumentNullException.ThrowIfNull(factory);

            AddHttpClient(services);

            var builder = new DefaultHttpClientBuilder(services, name);
            builder.AddTypedClient<TClient>(factory);
            return builder;
        }
    }
}
