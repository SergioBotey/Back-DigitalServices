using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using digital_services.Services.Input;
using digital_services.Services.Output;
using digital_services.Services.Process;
using digital_services.Services.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace digital_services
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy("AllowAllOrigins",
                    builder =>
                    {
                        builder
                            .AllowAnyOrigin()
                            .AllowAnyMethod()
                            .AllowAnyHeader();
                    });
            });

            // Configura el límite de longitud para el cuerpo multipart a 900 MB
            services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(x =>
            {
                x.MultipartBodyLengthLimit = 943718400; // 900 MB
            });

            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_2);

            //BD
            //var serverName = "10.20.103.132,58526\\SQLEXPRESS";
            //var databaseName = "ACR";
            //var userId = "usuRemoto";
            //var password = "dgTax";
            //var connectionString = $"Server={serverName};Database={databaseName};User Id={userId};Password={password};TrustServerCertificate=true;";
            //services.AddSingleton(new DatabaseService(connectionString));

            var connectionString1 = Configuration.GetConnectionString("Database1");
            var connectionString2 = Configuration.GetConnectionString("Database2");
            var connectionString3 = Configuration.GetConnectionString("Database3");
            services.AddSingleton(new DatabaseConfig(connectionString1, connectionString2, connectionString3));

            var directoriesConfig = Configuration.GetSection("Directories").Get<DirectoriesConfiguration>();
            services.Configure<DirectoriesConfiguration>(Configuration.GetSection("Directories"));
            services.Configure<ApiSettings>(Configuration.GetSection("ApiSettings"));
            services.AddSingleton(directoriesConfig);

            services.AddScoped<InputService>();
            services.AddScoped<ProcessService>();
            services.AddScoped<OutputService>();
            services.AddScoped<ValidationService>();
            services.AddScoped<TokenValidationService>();
            services.AddHttpClient();

            /*
            var connectionStrings = Configuration.GetSection("ConnectionStrings").GetChildren().ToDictionary(x => x.Key, x => x.Value);
            foreach (var connectionString in connectionStrings)
            {
                services.AddSingleton(new DatabaseService(connectionString.Value));
            }*/
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseCors("AllowAllOrigins"); // Aquí aplicamos la política de CORS
            //app.UseMiddleware<PythonMiddleware>();

            //Habilitar esto para producción, deshabilitar para local
            //app.UseHttpsRedirection();

            app.UseMvc();
        }
    }
}
