﻿using System;
using System.Linq;
using System.Reflection;
using Umbraco.Core;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Web;

namespace Our.Umbraco.Ditto
{
    /// <summary>
    /// The Umbraco property processor attribute.
    /// </summary>
    public class UmbracoPropertyAttribute : DittoProcessorAttribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UmbracoPropertyAttribute"/> class.
        /// </summary>
        public UmbracoPropertyAttribute()
        {
            PropertySource = Ditto.DefaultPropertySource;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="UmbracoPropertyAttribute"/> class.
        /// </summary>
        /// <param name="propertyName">Name of the property.</param>
        /// <param name="altPropertyName">Name of the alternative property.</param>
        /// <param name="recursive">If set to <c>true</c>, a recursive lookup is performed.</param>
        /// <param name="defaultValue">The default value.</param>
        public UmbracoPropertyAttribute(
            string propertyName, 
            string altPropertyName = null, 
            bool recursive = false, 
            object defaultValue = null)
        {
            this.PropertyName = propertyName;
            this.AltPropertyName = altPropertyName;
            this.Recursive = recursive;
            this.DefaultValue = defaultValue;

            PropertySource = Ditto.DefaultPropertySource;
        }

        /// <summary>
        /// Gets or sets the name of the property.
        /// </summary>
        /// <value>
        /// The name of the property.
        /// </value>
        public string PropertyName { get; set; }

        /// <summary>
        /// Gets or sets the name of the alternative property.
        /// </summary>
        /// <value>
        /// The name of the property.
        /// </value>
        public string AltPropertyName { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this <see cref="UmbracoPropertyAttribute"/> is recursive.
        /// </summary>
        /// <value>
        ///   <c>true</c> if recursive; otherwise, <c>false</c>.
        /// </value>
        public bool Recursive { get; set; }

        /// <summary>
        /// Gets or sets the default value.
        /// </summary>
        /// <value>
        /// The default value.
        /// </value>
        public object DefaultValue { get; set; }

        /// <summary>
        /// Gets or sets the property source from which to map values from
        /// </summary>
        public PropertySource PropertySource { get; set; }

        /// <summary>
        /// Processes the value.
        /// </summary>
        /// <returns>
        /// The <see cref="object" /> representing the processed value.
        /// </returns>
        public override object ProcessValue()
        {
            var defaultValue = this.DefaultValue;

            var recursive = this.Recursive;
            var propName = this.Context.PropertyDescriptor != null ? this.Context.PropertyDescriptor.Name : string.Empty;
            var altPropName = string.Empty;

            // Check for umbraco properties attribute on class
            if (this.Context.TargetType != null)
            {
                var classAttr = this.Context.TargetType
                    .GetCustomAttribute<UmbracoPropertiesAttribute>();
                if (classAttr != null)
                {
                    // Apply the prefix
                    if (!string.IsNullOrWhiteSpace(classAttr.Prefix))
                    {
                        altPropName = propName;
                        propName = classAttr.Prefix + propName;
                    }

                    // Apply global recursive setting
                    recursive |= classAttr.Recursive;

                    // Apply property source only if it's different from the default,
                    // and the current value is the default. We only do it this
                    // way because if they change it at the property level, we 
                    // want that to take precedence over the class level.
                    if (classAttr.PropertySource != Ditto.DefaultPropertySource
                        && PropertySource == Ditto.DefaultPropertySource)
                    {
                        PropertySource = classAttr.PropertySource;
                    }
                }
            }

            var umbracoPropertyName = this.PropertyName ?? propName;
            var altUmbracoPropertyName = this.AltPropertyName ?? altPropName;

            var content = this.Value as IPublishedContent;
            if (content == null)
            {
                return defaultValue;
            }
            
            object propertyValue = null;

            // Try fetching the value.
            if (!umbracoPropertyName.IsNullOrWhiteSpace())
            {
                propertyValue = GetPropertyValue(content, umbracoPropertyName, recursive);
            }

            // Try fetching the alt value.
            if ((propertyValue == null || propertyValue.ToString().IsNullOrWhiteSpace())
                && !string.IsNullOrWhiteSpace(altUmbracoPropertyName))
            {
                propertyValue = GetPropertyValue(content, altUmbracoPropertyName, recursive);
            }

            // Try setting the default value.
            if ((propertyValue == null || propertyValue.ToString().IsNullOrWhiteSpace())
                && defaultValue != null)
            {
                propertyValue = defaultValue;
            }

            return propertyValue;
        }

        /// <summary>
        /// Gets a property value from the given content object
        /// </summary>
        /// <param name="content"></param>
        /// <param name="umbracoPropertyName"></param>
        /// <param name="recursive"></param>
        /// <returns></returns>
        private object GetPropertyValue(IPublishedContent content, string umbracoPropertyName, bool recursive)
        {
            object propertyValue = null;

            if (PropertySource == PropertySource.InstanceProperties || PropertySource == PropertySource.InstanceThenUmbracoProperties)
            {
                propertyValue = GetClassPropertyValue(content, umbracoPropertyName);
            }

            if ((propertyValue == null || propertyValue.ToString().IsNullOrWhiteSpace())
                && (PropertySource != PropertySource.InstanceProperties))
            {
                propertyValue = GetUmbracoPropertyValue(content, umbracoPropertyName, recursive);
            }

            if ((propertyValue == null || propertyValue.ToString().IsNullOrWhiteSpace())
                && PropertySource == PropertySource.UmbracoThenInstanceProperties)
            {
                propertyValue = GetClassPropertyValue(content, umbracoPropertyName);
            }

            return propertyValue;
        }

        /// <summary>
        /// Gets a property value from the given content objects class properties
        /// </summary>
        /// <param name="content"></param>
        /// <param name="umbracoPropertyName"></param>
        /// <returns></returns>
        private object GetClassPropertyValue(IPublishedContent content, string umbracoPropertyName)
        {
            var contentType = content.GetType();
            var contentProperty = contentType.GetProperty(umbracoPropertyName, Ditto.MappablePropertiesBindingFlags);
            if (contentProperty != null && contentProperty.IsMappable())
            {
                if (Ditto.IsDebuggingEnabled 
                    && PropertySource == PropertySource.InstanceThenUmbracoProperties 
                    && Ditto.IPublishedContentProperties.Any(x => x.Name.InvariantEquals(umbracoPropertyName))
                    && content.HasProperty(umbracoPropertyName))
                {
                    // Property is an IPublishedContent property and an umbraco property exists so warn the user
                    LogHelper.Warn<UmbracoPropertyAttribute>("The property "+ umbracoPropertyName + " being mapped from content type " + contentType.Name + "'s instance properties hides a property in the umbraco properties collection of the same name. It is recommended that you avoid using umbraco property aliases that conflict with IPublishedContent instance property names, but if you can't avoid this and you require access to the hidden property you can use the PropertySource parameter of the processors attribute to override the order in which properties are checked.");
                }

                // This is more than 2x as fast as propertyValue = contentProperty.GetValue(content, null);
                return PropertyInfoInvocations.GetValue(contentProperty, content);
            }

            return null;
        }

        /// <summary>
        /// Gets a property value from the given content objects umbraco properties collection
        /// </summary>
        /// <param name="content"></param>
        /// <param name="umbracoPropertyName"></param>
        /// <param name="recursive"></param>
        /// <returns></returns>
        private object GetUmbracoPropertyValue(IPublishedContent content, string umbracoPropertyName, bool recursive)
        {
            if (Ditto.IsDebuggingEnabled
                && PropertySource == PropertySource.UmbracoThenInstanceProperties
                && Ditto.IPublishedContentProperties.Any(x => x.Name.InvariantEquals(umbracoPropertyName))
                && content.HasProperty(umbracoPropertyName))
            {
                // Property is an IPublishedContent property and an umbraco property exists so warn the user
                LogHelper.Warn<UmbracoPropertyAttribute>("The property " + umbracoPropertyName + " being mapped from the umbraco properties collection hides an instance property of the same name on content type " + content.GetType().Name + ". It is recommended that you avoid using umbraco property aliases that conflict with IPublishedContent instance property names, but if you can't avoid this and you require access to the hidden property you can use the PropertySource parameter of the processors attribute to override the order in which properties are checked.");
            }

            return content.GetPropertyValue(umbracoPropertyName, recursive);
        }
    }

    /// <summary>
    /// Defines the source from which the <see cref="UmbracoPropertyAttribute"/> should map values from
    /// </summary>
    public enum PropertySource
    {
        /// <summary>
        /// Properties declared on the content instance only
        /// </summary>
        InstanceProperties,

        /// <summary>
        /// Properties declared in the umbraco properties collection only
        /// </summary>
        UmbracoProperties,

        /// <summary>
        /// Properties declared on the content instance, followed by properties in the umbraco properties collection if no value is found
        /// </summary>
        InstanceThenUmbracoProperties,

        /// <summary>
        /// Properties declared in the umbraco properties collection, followed by the content instance properties if no value is found
        /// </summary>
        UmbracoThenInstanceProperties
    }
}