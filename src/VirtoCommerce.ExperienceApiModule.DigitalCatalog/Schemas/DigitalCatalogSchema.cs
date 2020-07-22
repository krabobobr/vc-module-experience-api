using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GraphQL;
using GraphQL.Builders;
using GraphQL.DataLoader;
using GraphQL.Resolvers;
using GraphQL.Types;
using GraphQL.Types.Relay;
using GraphQL.Types.Relay.DataObjects;
using MediatR;
using VirtoCommerce.ExperienceApiModule.Core.Extensions;
using VirtoCommerce.ExperienceApiModule.Core.Helpers;
using VirtoCommerce.ExperienceApiModule.Core.Infrastructure;
using VirtoCommerce.Platform.Core.Common;
using VirtoCommerce.XDigitalCatalog.Queries;

namespace VirtoCommerce.XDigitalCatalog.Schemas
{
    public class DigitalCatalogSchema : ISchemaBuilder
    {
        private readonly IMediator _mediator;
        private readonly IDataLoaderContextAccessor _dataLoader;

        public DigitalCatalogSchema(IMediator mediator, IDataLoaderContextAccessor dataLoader)
        {
            _mediator = mediator;
            _dataLoader = dataLoader;
        }

        public void Build(ISchema schema)
        {
            //We can't use the fluent syntax for new types registration provided by dotnet graphql here, because we have the strict requirement for underlying types extensions
            //and must use GraphTypeExtenstionHelper to resolve the effective type on execution time
            var productField = new FieldType
            {
                Name = "product",
                Arguments = new QueryArguments(new QueryArgument<NonNullGraphType<StringGraphType>> { Name = "id", Description = "id of the product" }),
                Type = GraphTypeExtenstionHelper.GetActualType<ProductType>(),
                Resolver = new AsyncFieldResolver<object>(async context =>
                {
                    var loader = _dataLoader.Context.GetOrAddBatchLoader<string, ExpProduct>("productsLoader", (ids) => LoadProductsAsync(_mediator, ids, context.SubFields.Values.GetAllNodesPaths()));
                    return await loader.LoadAsync(context.GetArgument<string>("id"));
                })
            };
            schema.Query.AddField(productField);

            //var productsConnectionBuilder = ConnectionBuilder.Create<ProductType, EdgeType<ProductType>, ProductsConnectonType<ProductType>, object>()
            var productsConnectionBuilder = GraphTypeExtenstionHelper.CreateConnection<ProductType, EdgeType<ProductType>, ProductsConnectonType<ProductType>, object>()
                .Name("products")
                .Argument<StringGraphType>("storeId", "The store id where products are searched")
                .Argument<StringGraphType>("lang", "The language for which all localized product data will be returned")
                .Argument<StringGraphType>("customerId", "The customer id for search result impersonalization")
                .Argument<StringGraphType>("currency", "The currency for which all prices data will be returned")
                .Argument<StringGraphType>("query", "The query parameter performs the full-text search")
                .Argument<StringGraphType>("filter", "This parameter applies a filter to the query results")
                .Argument<BooleanGraphType>("fuzzy", "When the fuzzy query parameter is set to true the search endpoint will also return products that contain slight differences to the search text.")
                .Argument<IntGraphType>("fuzzyLevel", "The fuzziness level is quantified in terms of the Damerau-Levenshtein distance, this distance being the number of operations needed to transform one word into another.")
                .Argument<StringGraphType>("facet", "Facets calculate statistical counts to aid in faceted navigation.")
                .Argument<StringGraphType>("sort", "The sort expression")
                .Argument<ListGraphType<StringGraphType>>("productIds", "Product Ids") // TODO: make something good with it, move productIds in filter for example
                .Unidirectional()
                .PageSize(20);

            productsConnectionBuilder.ResolveAsync(async context => await ResolveConnectionAsync(_mediator, context));

            schema.Query.AddField(productsConnectionBuilder.FieldType);
        }

        public static async Task<IDictionary<string, ExpProduct>> LoadProductsAsync(IMediator mediator, IEnumerable<string> ids, IEnumerable<string> includeFields)
        {
            var response = await mediator.Send(new LoadProductQuery { Ids = ids.ToArray(), IncludeFields = includeFields });
            return response.Products.ToDictionary(x => x.Id);
        }

        private static async Task<object> ResolveConnectionAsync(IMediator mediator, IResolveConnectionContext<object> context)
        {
            var first = context.First;
            var skip = Convert.ToInt32(context.After ?? 0.ToString());
            var includeFields = context.SubFields.Values.GetAllNodesPaths().Select(x => x.TrimStart("items.")).ToArray();

            // TODO: maybe we need to save it to UserContext?
            var storeId = context.GetArgument<string>("storeId");
            var customerId = context.GetArgument<string>("customerId");
            var currency = context.GetArgument<string>("currency");
            var lang = context.GetArgument<string>("lang");

            var productIds = context.GetArgument<List<string>>("productIds");

            var request = new SearchProductQuery
            {
                Lang = lang,
                StoreId = storeId,
                CustomerId = customerId,
                Currency = currency,
                IncludeFields = includeFields.ToArray(),
            };

            if (productIds.IsNullOrEmpty())
            {
                request.Skip = skip;
                request.Take = first ?? context.PageSize ?? 10;
                request.Query = context.GetArgument<string>("query");
                request.Filter = context.GetArgument<string>("filter");
                request.Facet = context.GetArgument<string>("facet");
                request.Fuzzy = context.GetArgument<bool>("fuzzy");
                request.FuzzyLevel = context.GetArgument<int?>("fuzzyLevel");
                request.Sort = context.GetArgument<string>("sort");
            }
            else
            {
                request.ProductIds = productIds;
                request.Take = productIds.Count;
            }

            var response = await mediator.Send(request);

            return new ProductsConnection<ExpProduct>()
            {
                Edges = response.Results
                    .Select((x, index) =>
                        new Edge<ExpProduct>()
                        {
                            Cursor = (skip + index).ToString(),
                            Node = x,
                        })
                    .ToList(),
                PageInfo = new PageInfo()
                {
                    HasNextPage = response.TotalCount > skip + first,
                    HasPreviousPage = skip > 0,
                    StartCursor = skip.ToString(),
                    EndCursor = Math.Min(response.TotalCount, (int)(skip + first)).ToString()
                },
                TotalCount = response.TotalCount,
                Facets = response.Facets
            };
        }
    }
}
