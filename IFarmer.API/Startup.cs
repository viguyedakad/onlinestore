﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autofac;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyModel;
using RS2.Core;
using IFarmer.Model;
using IFarmer.Entity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Threading.Tasks;

namespace onlinestore
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public IContainer ApplicationContainer { get; private set; }

        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddMvc();
            services.AddScoped<DbContext, OnlineshopdataContext>();

            services.AddDbContext<OnlineshopdataContext>(options =>
                options.UseMySql(Configuration.GetConnectionString("DefaultConnection")));

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseMySql(Configuration.GetConnectionString("DefaultConnection")));

            // Identity for authentication 
            services.AddIdentity<ApplicationUser, IdentityRole>()
                     .AddEntityFrameworkStores<ApplicationDbContext>()
                     .AddDefaultTokenProviders();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            }).AddCookie(options =>
           {
               options.LoginPath = "/Account/Login";
               // If the AccessDeniedPath isn't set, ASP.NET Core defaults 
               // the path to /Account/AccessDenied.
               options.AccessDeniedPath = "/Account/AccessDenied";
           });

            services.AddAuthentication().AddFacebook(facebookOptions =>
            {
                facebookOptions.AppId = Configuration["Authentication:Facebook:AppId"];
                facebookOptions.AppSecret = Configuration["Authentication:Facebook:AppSecret"];
            });

            services.AddScoped<IUnitOfWork, UnitOfWork>();

            //Di register using assembly -> aspnet core     
            var asseblies = GetLoadedAssemblies()
                                .SelectMany(x => x.DefinedTypes.Where(dt => !dt.IsAbstract && dt.IsClass && !dt.IsInterface));

            var dmComponents = asseblies.Where(a => a.GetTypeInfo().GetCustomAttributes().OfType<IDmComponent>().Any());

            var builder = new ContainerBuilder();

            foreach (var type in dmComponents)
            {
                var typeInterface = type.GetTypeInfo().ImplementedInterfaces.First(i => i.Name.Contains(type.Name));

                // TODO : Get type scope and assign to the life time 
                // var typeScope = type.GetTypeInfo().GetCustomAttributes().First();

                // Autofac registration     
                // builder.RegisterType(type).As(typeInterface);
                // .net core registration 
                services.Add(new ServiceDescriptor(typeInterface, type, ServiceLifetime.Transient));
            }

            //builder.Build();
            // Create the IServiceProvider based on the container.
            return null;
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IServiceProvider serviceProvider)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            //Cors
            app.UseCors(builder =>
            {
                builder.AllowAnyHeader();
                builder.AllowAnyMethod();
                builder.AllowCredentials();
                builder.AllowAnyOrigin(); // For anyone access.
            });

            app.UseAuthentication();

            app.UseMvc();

            // TODO : This does not need to be here everytime
            // CreateRolesAndAdminUser(serviceProvider);
        }

        private static Assembly[] GetLoadedAssemblies()
        {
            IEnumerable<Assembly> assemblies = null;
            var dependencyAssemblies = Enumerable.Empty<Assembly>();

            assemblies = DependencyContext.Default.GetDefaultAssemblyNames()
                                          .Where(a => a.Name.StartsWith("IFarmer.")
                                                || a.Name.StartsWith("Stwo."))
                                          .Select(a => Assembly.Load(a));

            return assemblies.ToArray();
        }

        private void CreateRolesAndAdminUser(IServiceProvider serviceProvider)
        {
            string[] roleNames = { "admin", "buyer", "producer" };

            foreach (string roleName in roleNames)
            {
                CreateRole(serviceProvider, roleName);
            }

            AddUserToRole(serviceProvider, Configuration["RoleSetting:DefaultAdmin:email"], Configuration["RoleSetting:DefaultAdmin:password"], "admin");
            AddUserToRole(serviceProvider, Configuration["RoleSetting:DefaultBuyer:email"], Configuration["RoleSetting:DefaultBuyer:password"], "buyer");
            AddUserToRole(serviceProvider, Configuration["RoleSetting:DefaultProducer:email"], Configuration["RoleSetting:DefaultProducer:password"], "producer");
        }

        /// <summary>
        /// Create a role if not exists.
        /// </summary>
        /// <param name="serviceProvider">Service Provider</param>
        /// <param name="roleName">Role Name</param>
        private static void CreateRole(IServiceProvider serviceProvider, string roleName)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            Task<bool> roleExists = roleManager.RoleExistsAsync(roleName);
            roleExists.Wait();

            if (!roleExists.Result)
            {
                Task<IdentityResult> roleResult = roleManager.CreateAsync(new IdentityRole(roleName));
                roleResult.Wait();
            }
        }

        /// <summary>
        /// Add user to a role if the user exists, otherwise, create the user and adds him to the role.
        /// </summary>
        /// <param name="serviceProvider">Service Provider</param>
        /// <param name="userEmail">User Email</param>
        /// <param name="userPwd">User Password. Used to create the user if not exists.</param>
        /// <param name="roleName">Role Name</param>
        private static void AddUserToRole(IServiceProvider serviceProvider, string userEmail,
            string userPwd, string roleName)
        {
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            Task<ApplicationUser> checkAppUser = userManager.FindByEmailAsync(userEmail);
            checkAppUser.Wait();

            ApplicationUser appUser = checkAppUser.Result;

            if (checkAppUser.Result == null)
            {
                ApplicationUser newAppUser = new ApplicationUser
                {
                    Email = userEmail,
                    UserName = userEmail
                };

                Task<IdentityResult> taskCreateAppUser = userManager.CreateAsync(newAppUser, userPwd);
                taskCreateAppUser.Wait();

                if (taskCreateAppUser.Result.Succeeded)
                {
                    appUser = newAppUser;
                }
            }

            Task<IdentityResult> newUserRole = userManager.AddToRoleAsync(appUser, roleName);
            newUserRole.Wait();
        }

    }
}
