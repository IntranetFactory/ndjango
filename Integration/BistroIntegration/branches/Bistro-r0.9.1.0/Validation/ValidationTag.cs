using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bistro.Validation;

namespace NDjango.BistroIntegration.Validation
{
    /// <summary>
    /// Default implementation of the {% validate %} tag
    /// </summary>
    class ValidationTag: NDjango.Compatibility.SimpleTag
    {
        public ValidationTag() : base(false, "validate", -1) { }

        /// <summary>
        /// Processes the tag.
        /// </summary>
        /// <param name="content">The content.</param>
        /// <param name="parms">The parms.</param>
        /// <returns></returns>
        public override string ProcessTag(NDjango.Interfaces.IContext context, string content, object[] parms)
        {
            IValidator v = null;
            foreach (string ns in parms)
                v =
                    v == null ?
                    ValidationRepository.Instance.GetValidatorForNamespace(ns) :
                    v.Merge(ValidationRepository.Instance.GetValidatorForNamespace(ns));

            return
                new StringBuilder()
                    .AppendLine("\r\n<script type=\"text/javascript\">")
                    .AppendLine("if (validation == undefined) var validation = new Array();")
                    .AppendLine(JSEmitter.Instance.Emit(v, "validation[\"" + v.Name + "\"]"))
                    .AppendLine("</script>")
                    .ToString();
        }
    }
}
