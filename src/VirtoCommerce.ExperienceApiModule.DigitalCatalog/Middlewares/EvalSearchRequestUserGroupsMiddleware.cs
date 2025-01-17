using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PipelineNet.Middleware;
using VirtoCommerce.CustomerModule.Core.Model;
using VirtoCommerce.CustomerModule.Core.Services;
using VirtoCommerce.ExperienceApiModule.Core;
using VirtoCommerce.ExperienceApiModule.XDigitalCatalog.Index;
using VirtoCommerce.Platform.Core.Common;

namespace VirtoCommerce.XDigitalCatalog.Middlewares
{
    public class EvalSearchRequestUserGroupsMiddleware : IAsyncMiddleware<IndexSearchRequestBuilder>
    {
        protected readonly IMemberResolver _memberResolver;
        protected readonly IMemberService _memberService;

        public EvalSearchRequestUserGroupsMiddleware(IMemberResolver memberResolver, IMemberService memberService)
        {
            _memberResolver = memberResolver;
            _memberService = memberService;
        }

        public async Task Run(IndexSearchRequestBuilder parameter, Func<IndexSearchRequestBuilder, Task> next)
        {
            var userGroups = new List<string> { "__any" };

            if (!string.IsNullOrEmpty(parameter?.UserId) && !AnonymousUser.UserName.EqualsInvariant(parameter.UserId))
            {
                var member = await _memberResolver.ResolveMemberByIdAsync(parameter.UserId);

                if (member is Contact contact)
                {
                    userGroups.AddRange(await GetUserGroupsInheritedAsync(contact));
                }
            }

            var userGroupsValue = string.Join(',', userGroups);
            parameter.AddTerms(new[] { $"user_groups:{userGroupsValue}" });

            await next(parameter);
        }

        private async Task<IList<string>> GetUserGroupsInheritedAsync(Contact contact)
        {
            var userGroups = new List<string>();

            if (!contact.Groups.IsNullOrEmpty())
            {
                userGroups.AddRange(contact.Groups);
            }

            if (!contact.Organizations.IsNullOrEmpty())
            {
                var organizations = await _memberService.GetByIdsAsync(contact.Organizations.ToArray(), MemberResponseGroup.WithGroups.ToString());
                userGroups.AddRange(organizations.OfType<Organization>().SelectMany(x => x.Groups));
            }

            return userGroups;
        }
    }
}
