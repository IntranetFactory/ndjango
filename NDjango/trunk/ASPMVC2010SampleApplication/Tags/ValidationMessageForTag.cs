﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using NDjango.Interfaces;
using System.Web.Mvc;
using System.Web.Mvc.Html;

namespace ASPMVC2010SampleApplication
{
    [Name("validation-message-for")]
    public class ValidationMessageForlTag : NDjango.Compatibility.SimpleTag
    {
        public ValidationMessageForlTag()
            : base(false, 1)
        { }

        public override string ProcessTag(NDjango.Interfaces.IContext context, string content, object[] parms)
        {
            var htmlHelperOption = context.tryfind("Html");
            if (htmlHelperOption == null)
                return "";

            var htmlHelper = (HtmlHelper)htmlHelperOption.Value;

            var metadata_provider = new DataAnnotationsModelMetadataProvider();

            if (context.ModelType == null)
                htmlHelper.ViewData.ModelMetadata = null;
            else
            {
                var model = Activator.CreateInstance(context.ModelType.Value);
                htmlHelper.ViewData.ModelMetadata = metadata_provider.GetMetadataForType(() => model, context.ModelType.Value);
            }

            var result = htmlHelper.ValidationMessage(parms[0].ToString());
            if (result == null)
                return "";
            else
                return result.ToHtmlString();
        }
    }
}