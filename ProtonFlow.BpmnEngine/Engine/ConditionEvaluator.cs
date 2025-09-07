using BpmnEngine.Models;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BpmnEngine.Engine
{
    public static class ConditionEvaluator
    {
        public static bool HasCondition(XElement flow)
        {
            return flow.Descendants().Any(e => e.Name.LocalName == "conditionExpression");
        }

        public static bool EvaluateCondition(XElement flow, ProcessInstance instance)
        {
            var condEl = flow.Descendants().FirstOrDefault(e => e.Name.LocalName == "conditionExpression");
            if (condEl == null)
                return false;

            var expr = condEl.Value?.Trim() ?? string.Empty;
            if (expr.StartsWith("${") && expr.EndsWith("}"))
            {
                expr = expr.Substring(2, expr.Length - 3).Trim();
            }

            // Support simple expressions: variable OP number
            var m = Regex.Match(expr, @"^\s*([a-zA-Z_][a-zA-Z0-9_]*)\s*(==|!=|>=|<=|>|<)\s*([-+]?[0-9]+(?:\.[0-9]+)?)\s*$");
            if (!m.Success)
                return false;

            var varName = m.Groups[1].Value;
            var op = m.Groups[2].Value;
            var rightText = m.Groups[3].Value;

            if (!instance.Variables.TryGetValue(varName, out var leftObj) || leftObj is null)
                return false;

            if (!double.TryParse(rightText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var right))
                return false;

            double left;
            try
            {
                if (leftObj is IConvertible)
                {
                    left = Convert.ToDouble(leftObj, System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (leftObj is string s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tmp))
                {
                    left = tmp;
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            return op switch
            {
                ">" => left > right,
                ">=" => left >= right,
                "<" => left < right,
                "<=" => left <= right,
                "==" => Math.Abs(left - right) < double.Epsilon,
                "!=" => Math.Abs(left - right) > double.Epsilon,
                _ => false
            };
        }
    }
}
