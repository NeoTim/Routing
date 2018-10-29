﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Authorization
{
    public class AuthorizationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IAuthorizationPolicyProvider _policyProvider;

        public AuthorizationMiddleware(RequestDelegate next, IAuthorizationPolicyProvider policyProvider)
        {
            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            if (policyProvider == null)
            {
                throw new ArgumentNullException(nameof(policyProvider));
            }

            _next = next;
            _policyProvider = policyProvider;
        }

        public async Task Invoke(HttpContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var endpoint = context.Features.Get<IEndpointFeature>()?.Endpoint;
            if (endpoint == null)
            {
                await _next(context);
                return;
            }

            var policyEvaluator = context.RequestServices.GetRequiredService<IPolicyEvaluator>();

            // REVIEW: Does order matter? Is ordered metadata in the correct order?
            var authorizeData = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            var policy = await AuthorizationPolicy.CombineAsync(_policyProvider, authorizeData);
            if (policy == null)
            {
                await _next(context);
                return;
            }

            var authenticateResult = await policyEvaluator.AuthenticateAsync(policy, context);

            // Allow Anonymous skips all authorization
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null)
            {
                await _next(context);
                return;
            }

            var authorizeResult = await policyEvaluator.AuthorizeAsync(policy, authenticateResult, context, endpoint);

            if (authorizeResult.Challenged)
            {
                if (policy.AuthenticationSchemes.Any())
                {
                    foreach (var scheme in policy.AuthenticationSchemes)
                    {
                        await context.ChallengeAsync(scheme);
                    }
                }
                else
                {
                    await context.ChallengeAsync();
                }

                return;
            }
            else if (authorizeResult.Forbidden)
            {
                if (policy.AuthenticationSchemes.Any())
                {
                    foreach (var scheme in policy.AuthenticationSchemes)
                    {
                        await context.ForbidAsync(scheme);
                    }
                }
                else
                {
                    await context.ForbidAsync();
                }

                return;
            }

            await _next(context);
        }
    }
}