using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Examine.Lucene;
using Examine.Lucene.Directories;
using Examine.Lucene.Providers;
using Lucene.Net.Analysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Examine
{
    public static class ServicesCollectionExtensions
    {
        /// <summary>
        /// Registers a file system based Lucene Examine index
        /// </summary>
        public static IServiceCollection AddExamineLuceneIndex(
            this IServiceCollection serviceCollection,
            string name,
            FieldDefinitionCollection fieldDefinitions = null,
            Analyzer analyzer = null,
            IValueSetValidator validator = null,
            IReadOnlyDictionary<string, IFieldValueTypeFactory> indexValueTypesFactory = null)
            => serviceCollection.AddExamineLuceneIndex<LuceneIndex>(name, fieldDefinitions, analyzer, validator, indexValueTypesFactory);

        /// <summary>
        /// Registers a file system based Lucene Examine index
        /// </summary>
        public static IServiceCollection AddExamineLuceneIndex<TIndex>(
            this IServiceCollection serviceCollection,
            string name,
            FieldDefinitionCollection fieldDefinitions = null,
            Analyzer analyzer = null,
            IValueSetValidator validator = null,
            IReadOnlyDictionary<string, IFieldValueTypeFactory> indexValueTypesFactory = null)
            where TIndex : LuceneIndex
            => serviceCollection.AddExamineLuceneIndex<IIndex, FileSystemDirectoryFactory>(name, null, fieldDefinitions, analyzer, validator, indexValueTypesFactory);

        /// <summary>
        /// Registers an Examine index
        /// </summary>
        public static IServiceCollection AddExamineLuceneIndex<TIndex, TDirectoryFactory>(
            this IServiceCollection serviceCollection,
            string name,
            Func<IServiceProvider, TDirectoryFactory> directoryFactory,
            FieldDefinitionCollection fieldDefinitions = null,
            Analyzer analyzer = null,
            IValueSetValidator validator = null,
            IReadOnlyDictionary<string, IFieldValueTypeFactory> indexValueTypesFactory = null)
            where TIndex : IIndex
            where TDirectoryFactory : class, IDirectoryFactory
        {
            if (directoryFactory != null)
            {
                serviceCollection.TryAddTransient<TDirectoryFactory>(directoryFactory);
            }

            // This is the long way to add IOptions but gives us access to the
            // services collection which we need to get the dir factory
            serviceCollection.AddSingleton<IConfigureOptions<LuceneDirectoryIndexOptions>>(
                services => new ConfigureNamedOptions<LuceneDirectoryIndexOptions>(
                    name,
                    (options) =>
                    {
                        options.Analyzer = analyzer;
                        options.Validator = validator;
                        options.IndexValueTypesFactory = indexValueTypesFactory;
                        options.FieldDefinitions = fieldDefinitions;
                        options.DirectoryFactory = services.GetRequiredService<TDirectoryFactory>();
                    }));

            return serviceCollection.AddSingleton<IIndex>(services =>
            {
                using (var scope = services.CreateScope())
                {
                    IOptionsSnapshot<LuceneDirectoryIndexOptions> options
                        = scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<LuceneDirectoryIndexOptions>>();

                    LuceneIndex index = ActivatorUtilities.CreateInstance<LuceneIndex>(
                        services,
                        new object[] { name, options });

                    return index;
                }
            });
        }

        /// <summary>
        /// Registers a standalone Examine searcher
        /// </summary>
        /// <typeparam name="TSearcher"></typeparam>
        /// <param name="serviceCollection"></param>
        /// <param name="name"></param>
        /// <param name="parameterFactory">
        /// A factory to fullfill the custom searcher construction parameters excluding the name that are not already registerd in DI.
        /// </param>
        /// <returns></returns>
        public static IServiceCollection AddExamineSearcher<TSearcher>(
            this IServiceCollection serviceCollection,
            string name,
            Func<IServiceProvider, IList<object>> parameterFactory)
            where TSearcher : ISearcher
           => serviceCollection.AddTransient<ISearcher>(services =>
           {
               IList<object> parameters = parameterFactory(services);
               parameters.Insert(0, name);

               TSearcher searcher = ActivatorUtilities.CreateInstance<TSearcher>(
                   services,
                   parameters.ToArray());

               return searcher;
           });

        /// <summary>
        /// Registers a lucene multi index searcher
        /// </summary>
        public static IServiceCollection AddExamineLuceneMultiSearcher(
            this IServiceCollection serviceCollection,
            string name,
            string[] indexNames,
            Analyzer analyzer = null)
            => serviceCollection.AddExamineSearcher<MultiIndexSearcher>(name, s =>
            {
                IEnumerable<IIndex> matchedIndexes = s.GetServices<IIndex>()
                     .Where(x => indexNames.Contains(x.Name));

                var parameters = new List<object>
                {
                    matchedIndexes
                };

                if (analyzer != null)
                {
                    parameters.Add(analyzer);
                }

                return parameters;
            });

        /// <summary>
        /// Adds the Examine core services
        /// </summary>
        /// <param name="services"></param>
        /// <returns></returns>
        public static IServiceCollection AddExamine(this IServiceCollection services, DirectoryInfo appRootDirectory = null)
        {
            services.AddSingleton<IApplicationRoot, CurrentEnvironmentApplicationRoot>();
            services.AddSingleton<IExamineManager, ExamineManager>();
            services.AddSingleton<IApplicationIdentifier, AspNetCoreApplicationIdentifier>();
            services.AddSingleton<ILockFactory, DefaultLockFactory>();
            services.AddSingleton<SyncMutexManager>();

            // each one needs to be ctor'd with a root dir, we'll allow passing that in or use the result of IApplicationRoot
            services.AddSingleton<SyncTempEnvDirectoryFactory>(
                s => ActivatorUtilities.CreateInstance<SyncTempEnvDirectoryFactory>(
                    s,
                    new[] { appRootDirectory ?? s.GetRequiredService<IApplicationRoot>().ApplicationRoot }));

            services.AddSingleton<TempEnvDirectoryFactory>(
                s => ActivatorUtilities.CreateInstance<TempEnvDirectoryFactory>(
                    s,
                    new[] { appRootDirectory ?? s.GetRequiredService<IApplicationRoot>().ApplicationRoot }));

            services.AddSingleton<FileSystemDirectoryFactory>(
                s => ActivatorUtilities.CreateInstance<FileSystemDirectoryFactory>(
                    s,
                    new[] { appRootDirectory ?? s.GetRequiredService<IApplicationRoot>().ApplicationRoot }));

            return services;
        }
    }
}