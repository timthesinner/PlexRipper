﻿using System.Reflection;
using Autofac;
using Autofac.Extras.Quartz;
using FluentValidation;
using MediatR;
using PlexRipper.Application.Common;
using PlexRipper.Domain.Behavior.Pipelines;
using Module = Autofac.Module;

namespace PlexRipper.Application.Config
{
    /// <summary>
    /// Used to register all dependancies in Autofac for the Application project.
    /// </summary>
    public class ApplicationModule : Module
    {
        /// <inheritdoc/>
        protected override void Load(ContainerBuilder builder)
        {
            var assembly = Assembly.GetExecutingAssembly();

            // Register the Command's Validators (Validators based on FluentValidation library)
            builder.RegisterAssemblyTypes(assembly)
                .Where(t => t.IsClosedTypeOf(typeof(IValidator<>)))
                .AsImplementedInterfaces();

            // Register all the Command classes (they implement IRequestHandler) in assembly holding the Commands
            builder.RegisterAssemblyTypes(assembly)
                .AsClosedTypesOf(typeof(IRequestHandler<,>));

            // Register Behavior Pipeline
            builder.RegisterGeneric(typeof(ValidationPipeline<,>)).As(typeof(IPipelineBehavior<,>));

            builder.RegisterType<PlexDownloadTaskFactory>().As<IPlexDownloadTaskFactory>().InstancePerLifetimeScope();

            // register all I*Services
            builder.RegisterAssemblyTypes(assembly)
                .Where(t => t.Name.EndsWith("Service"))
                .AsImplementedInterfaces()
                .InstancePerDependency();

            // Register Quartz dependancies
            // Source: https://stackoverflow.com/q/16908342/8205497
            // builder.Register((x) => new StdSchedulerFactory().GetScheduler()).As<IScheduler>();
            //
            // builder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly()).Where(x => typeof(IJob).IsAssignableFrom(x));
            //
            // builder.Register(context => new AutofacJobFactory(context)).SingleInstance();

            builder.RegisterModule(new QuartzAutofacFactoryModule());

            builder.RegisterModule(new QuartzAutofacJobsModule(assembly));
        }
    }
}