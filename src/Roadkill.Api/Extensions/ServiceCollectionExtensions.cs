using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MailKit;
using MailKit.Net.Smtp;
using Marten.AspNetIdentity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NSwag;
using NSwag.SwaggerGeneration.Processors.Security;
using Roadkill.Api.JWT;
using Roadkill.Api.Settings;
using Roadkill.Core.Authorization;
using Roadkill.Core.Extensions;
using Roadkill.Text;
using Roadkill.Text.Sanitizer;
using Roadkill.Text.TextMiddleware;
using Scrutor;

namespace Roadkill.Api.Extensions
{
	public static class ServiceCollectionExtensions
	{
		public static IServiceCollection ScanAndRegisterApi(this IServiceCollection services)
		{
			services.Scan(scan => scan
				.FromAssemblyOf<Startup>()
				.AddClasses()
				.UsingRegistrationStrategy(RegistrationStrategy.Skip)
				.AsMatchingInterface()
				.WithTransientLifetime());

			return services;
		}

		public static IServiceCollection AddMailkit(this IServiceCollection services)
		{
			services.AddScoped<IMailTransport, SmtpClient>();
			return services;
		}

		public static IServiceCollection AddMarkdown(this IServiceCollection services)
		{
			services.AddScoped<TextSettings>();
			services.AddScoped<IHtmlWhiteListProvider, HtmlWhiteListProvider>();
			services.AddScoped<ITextMiddlewareBuilder>(TextMiddlewareBuilder.Default);
			return services;
		}

		public static IServiceCollection AddJwtDefaults(this IServiceCollection services, IConfiguration configuration, ILogger logger)
		{
			var jwtSettings = services.AddConfigurationOf<JwtSettings>(configuration);
			if (jwtSettings == null)
			{
				logger.LogError($"No JWT settings were found. Do you have a JWT section in your configuration file?");
				throw new InvalidOperationException("No JWT settings were found. Do you have a JWT section in your configuration file?");
			}

			if (jwtSettings.Password.Length < 20)
			{
				logger.LogError($"The JWT.Password is under 20 characters in length.");
				throw new InvalidOperationException("The JWT.Password setting is under 20 characters in length.");
			}

			if (jwtSettings.ExpiresDays < 1)
			{
				logger.LogError($"The JWT.ExpiresDays is {jwtSettings.ExpiresDays}.");
				throw new InvalidOperationException("The JWT.ExpiresDays is setting should be 1 or greater.");
			}

			services.AddSingleton(jwtSettings);
			services.AddScoped<IJwtTokenProvider>(provider =>
			{
				var settings = provider.GetService<JwtSettings>();
				var tokenHandler = new JwtSecurityTokenHandler();
				return new JwtTokenProvider(settings, tokenHandler);
			});

			var password = Encoding.ASCII.GetBytes(jwtSettings.Password);
			services.AddAuthentication(x =>
			{
				x.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
				x.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
			})
			.AddJwtBearer(x =>
			{
				x.RequireHttpsMetadata = false;
				x.SaveToken = true;
				x.TokenValidationParameters = new TokenValidationParameters
				{
					ValidateIssuerSigningKey = true,
					IssuerSigningKey = new SymmetricSecurityKey(password),
					ValidateIssuer = false,
					ValidateAudience = false
				};
			});

			void ConfigureJwtClaimsPolicies(AuthorizationOptions options)
			{
				options.AddPolicy(PolicyNames.Admin, policy => policy.RequireClaim(ClaimTypes.Role, RoleNames.Admin));
				options.AddPolicy(PolicyNames.Editor, policy => policy.RequireClaim(ClaimTypes.Role, RoleNames.Editor));
			}

			services.AddAuthorization(ConfigureJwtClaimsPolicies);

			return services;
		}

		public static IServiceCollection AddIdentityDefaults(this IServiceCollection services)
		{
			services.AddIdentity<RoadkillUser, IdentityRole>(
					options =>
					{
						options.Password.RequireDigit = false;
						options.Password.RequiredUniqueChars = 0;
						options.Password.RequireLowercase = false;
						options.Password.RequiredLength = 0;
						options.Password.RequireNonAlphanumeric = false;
						options.Password.RequireUppercase = false;
					})
				.AddMartenStores<RoadkillUser, IdentityRole>()
				.AddDefaultTokenProviders();

			return services;
		}

		public static IServiceCollection AddMvcAndVersionedSwagger(this IServiceCollection services)
		{
			services
				.AddMvcCore()
				.AddDataAnnotations()
				.AddApiExplorer()
				.AddJsonFormatters()
				.SetCompatibilityVersion(CompatibilityVersion.Version_2_2)
				.AddVersionedApiExplorer(options =>
				{
					options.SubstituteApiVersionInUrl = true;
					options.GroupNameFormat = "VVV";
				});

			services.AddApiVersioning(options =>
			{
				options.ApiVersionReader = new UrlSegmentApiVersionReader();
				options.AssumeDefaultVersionWhenUnspecified = true;
			});

			services.AddSwaggerDocument(document =>
			{
				// Add an authenticate button to Swagger for JWT tokens
				document.OperationProcessors.Add(new OperationSecurityScopeProcessor("JWT"));
				var swaggerSecurityScheme = new SwaggerSecurityScheme
				{
					Type = SwaggerSecuritySchemeType.ApiKey,
					Name = "Authorization",
					In = SwaggerSecurityApiKeyLocation.Header,
					Description = "Type into the textbox: Bearer {your JWT token}. You can get a JWT token from /Authorization/Authenticate."
				};

				document.ApiGroupNames = new[] { "3" };
				document.DocumentName = "v3";
				document.DocumentProcessors.Add(new SecurityDefinitionAppender("JWT", swaggerSecurityScheme));
				document.PostProcess = d =>
				{
					d.Info.Title = "Roadkill API";
					d.Info.Version = "3.0";
				};
			});

			return services;
		}
	}
}