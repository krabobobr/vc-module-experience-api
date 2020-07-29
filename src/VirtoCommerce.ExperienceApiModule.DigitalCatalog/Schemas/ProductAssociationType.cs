using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GraphQL.DataLoader;
using GraphQL.Resolvers;
using GraphQL.Types;
using MediatR;
using VirtoCommerce.CatalogModule.Core.Model;
using VirtoCommerce.ExperienceApiModule.Core.Extensions;
using VirtoCommerce.ExperienceApiModule.Core.Helpers;
using VirtoCommerce.XDigitalCatalog.Queries;

namespace VirtoCommerce.XDigitalCatalog.Schemas
{
    public class ProductAssociationType : ObjectGraphType<ProductAssociation>
    {
        public ProductAssociationType(IDataLoaderContextAccessor dataLoader, IMediator mediator)
        {
            Name = "ProductAssociation";
            Description = "product association.";

            Field(d => d.Type);
            Field(d => d.Priority);
            Field("Quantity", x => x.Quantity, nullable: true, type: typeof(IntGraphType));
            Field(d => d.AssociatedObjectId);
            Field(d => d.AssociatedObjectType);
            Field<ListGraphType<StringGraphType>>("tags", resolve: context => context.Source.Tags.ToList());

            var productField = new FieldType
            {
                Name = "product",
                Type = GraphTypeExtenstionHelper.GetActualType<ProductType>(),
                Resolver = new AsyncFieldResolver<ProductAssociation, object>(async context =>
                {
                    var loader = dataLoader.Context.GetOrAddBatchLoader<string, ExpProduct>("associatedProductLoader", (ids) => LoadProductsAsync(mediator, ids, context.SubFields.Values.GetAllNodesPaths()));

                    // IMPORTANT: In order to avoid deadlocking on the loader we use the following construct (next 2 lines):
                    var loadHandle = loader.LoadAsync(context.Source.AssociatedObjectId);
                    return await loadHandle;
                })
            };
            AddField(productField);
        }

        public static async Task<IDictionary<string, ExpProduct>> LoadProductsAsync(IMediator mediator, IEnumerable<string> ids, IEnumerable<string> includeFields)
        {
            var response = await mediator.Send(new LoadProductQuery { Ids = ids.ToArray(), IncludeFields = includeFields });
            return response.Products.ToDictionary(x => x.Id);
        }
    }
}
