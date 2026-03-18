using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TaskFlow.Models
{
    /// <summary>
    /// 变量类型枚举
    /// </summary>
    public enum VariableType
    {
        Int,
        String,
        Bool,
        Double
    }

    /// <summary>
    /// 变量定义
    /// </summary>
    public partial class Variable : ObservableObject
    {
        private string _name = string.Empty;

        public string Name
        {
            get => _name;
            set
            {
                value ??= "";
                value = new string(value.Where(c => !char.IsPunctuation(c) && !char.IsSymbol(c)).ToArray());
                SetProperty(ref _name, value);
            }
        }

        [ObservableProperty]
        private VariableType _type = VariableType.Int;

        [ObservableProperty]
        private string _value = "0";

        /// <summary>
        /// 获取int值，如果类型不匹配或解析失败返回0
        /// </summary>
        public int GetIntValue()
        {
            return int.TryParse(Value, out int result) ? result : 0;
        }

        /// <summary>
        /// 获取string值
        /// </summary>
        public string GetStringValue()
        {
            return Value ?? string.Empty;
        }

        /// <summary>
        /// 获取bool值
        /// </summary>
        public bool GetBoolValue()
        {
            return bool.TryParse(Value, out bool result) && result;
        }

        /// <summary>
        /// 获取double值，如果类型不匹配或解析失败返回0
        /// </summary>
        public double GetDoubleValue()
        {
            return double.TryParse(Value, out double result) ? result : 0;
        }

        /// <summary>
        /// 设置值（自动根据类型校验）
        /// </summary>
        public void SetValue(string value)
        {
            if (Type == VariableType.Int)
            {
                // 尝试解析为int，失败则设为0
                Value = int.TryParse(value, out int intVal) ? intVal.ToString() : "0";
            }
            else if (Type == VariableType.Bool)
            {
                // 尝试解析为bool，失败则设为False
                Value = bool.TryParse(value, out bool boolVal) ? boolVal.ToString() : "False";
            }
            else if (Type == VariableType.Double)
            {
                // 尝试解析为double，失败则设为0
                Value = double.TryParse(value, out double dblVal) ? dblVal.ToString() : "0";
            }
            else
            {
                Value = value ?? string.Empty;
            }
        }
    }

    /// <summary>
    /// 全局变量仓库，管理所有变量
    /// </summary>
    public partial class VariableStore : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<Variable> _variables = new();

        /// <summary>
        /// 按名称获取变量值（用于表达式替换）
        /// </summary>
        public string? GetValue(string name)
        {
            var variable = Variables.FirstOrDefault(v => v.Name == name);
            return variable?.Value;
        }

        /// <summary>
        /// 按名称设置变量值
        /// </summary>
        public bool SetValue(string name, string value)
        {
            var variable = Variables.FirstOrDefault(v => v.Name == name);
            if (variable == null) return false;
            variable.SetValue(value);
            return true;
        }

        /// <summary>
        /// 添加变量
        /// </summary>
        public bool AddVariable(string name, VariableType type, string defaultValue = "")
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (Variables.Any(v => v.Name == name)) return false;

            // 根据类型验证并解析初始值
            string value;
            switch (type)
            {
                case VariableType.Int:
                    value = int.TryParse(defaultValue, out int iv) ? iv.ToString() : "0";
                    break;
                case VariableType.Double:
                    value = double.TryParse(defaultValue, out double dv) ? dv.ToString() : "0";
                    break;
                case VariableType.Bool:
                    value = bool.TryParse(defaultValue, out bool bv) ? bv.ToString() : "False";
                    break;
                default: // String
                    value = defaultValue;
                    break;
            }

            var variable = new Variable
            {
                Name = name,
                Type = type,
                Value = value
            };
            Variables.Add(variable);
            return true;
        }

        /// <summary>
        /// 删除变量
        /// </summary>
        public bool RemoveVariable(string name)
        {
            var variable = Variables.FirstOrDefault(v => v.Name == name);
            if (variable == null) return false;
            Variables.Remove(variable);
            return true;
        }

        /// <summary>
        /// 重命名变量
        /// </summary>
        public bool RenameVariable(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return false;
            if (Variables.Any(v => v.Name == newName)) return false;

            var variable = Variables.FirstOrDefault(v => v.Name == oldName);
            if (variable == null) return false;
            variable.Name = newName;
            return true;
        }

        /// <summary>
        /// 将表达式中的 @变量名 替换为实际值
        /// 规则：@后面紧跟的中英文字母、数字、下划线组成变量名
        /// </summary>
        /// <param name="expression">包含@变量引用的表达式</param>
        /// <param name="throwOnMissing">为true时，引用不存在的变量将抛出异常</param>
        public string ResolveVariableReferences(string expression, bool throwOnMissing = false)
        {
            if (string.IsNullOrEmpty(expression)) return expression;

            // 匹配 @变量名，变量名由中英文字母、数字、下划线组成
            var pattern = @"@([\w\u4e00-\u9fff]+)";
            return Regex.Replace(expression, pattern, match =>
            {
                string varName = match.Groups[1].Value;
                var variable = Variables.FirstOrDefault(v => v.Name == varName);
                if (variable == null)
                {
                    if (throwOnMissing)
                        throw new InvalidOperationException($"变量 @{varName} 不存在，请先在变量管理器中添加");
                    return match.Value; // 未找到变量，保留原文
                }

                if (variable.Type == VariableType.String)
                {
                    return $"\"{variable.GetStringValue()}\"";
                }
                if (variable.Type == VariableType.Bool)
                {
                    return variable.GetBoolValue() ? "true" : "false";
                }
                if (variable.Type == VariableType.Double)
                {
                    return variable.GetDoubleValue().ToString();
                }
                return variable.GetIntValue().ToString();
            });
        }

        /// <summary>
        /// 尝试解析赋值语句，格式: @变量名 = 表达式
        /// 返回是否为赋值语句
        /// </summary>
        public bool TryAssign(string expression, string resolvedRightSide)
        {
            // 匹配赋值格式：@VarName = ...
            var assignPattern = @"^\s*@([\w\u4e00-\u9fff]+)\s*=\s*(.+)$";
            var match = Regex.Match(expression, assignPattern);
            if (!match.Success) return false;

            string varName = match.Groups[1].Value;
            var variable = Variables.FirstOrDefault(v => v.Name == varName);
            if (variable == null) return false;

            // 获取去引号前的值，用于判断字符串是否是被双引号包裹的原生字面量
            string originalVal = resolvedRightSide.Trim();
            string finalVal = originalVal.Trim('"');

            // 类型校验
            switch (variable.Type)
            {
                case VariableType.Int:
                    if (originalVal.StartsWith("\"") && originalVal.EndsWith("\"") && originalVal.Length >= 2)
                        throw new InvalidOperationException($"类型不匹配：无法将字符串赋给 Int（整数）类型变量 @{varName}");

                    // 支持从浮点数强制转换为证书（直接截取整数部分）
                    if (double.TryParse(finalVal, out double dForInt))
                    {
                        finalVal = ((int)dForInt).ToString();
                    }
                    else if (!int.TryParse(finalVal, out _))
                    {
                        throw new InvalidOperationException($"无法将值 \"{finalVal}\" 赋给 Int 类型变量 @{varName}，值必须为数字");
                    }
                    break;
                case VariableType.Double:
                    if (originalVal.StartsWith("\"") && originalVal.EndsWith("\"") && originalVal.Length >= 2)
                        throw new InvalidOperationException($"类型不匹配：无法将字符串赋给 Double（小数）类型变量 @{varName}");

                    if (!double.TryParse(finalVal, out _))
                        throw new InvalidOperationException($"无法将值 \"{finalVal}\" 赋给 Double 类型变量 @{varName}，值必须为数字");
                    break;
                case VariableType.Bool:
                    if (originalVal.StartsWith("\"") && originalVal.EndsWith("\"") && originalVal.Length >= 2)
                        throw new InvalidOperationException($"类型不匹配：无法将字符串赋给 Bool（布尔）类型变量 @{varName}");

                    if (!bool.TryParse(finalVal, out _))
                        throw new InvalidOperationException($"无法将值 \"{finalVal}\" 赋给 Bool 类型变量 @{varName}，值必须为 True 或 False");
                    break;
                case VariableType.String:
                    // 向 String 变量赋值时，必须显式以双引号包裹，或者它是由其他返回值为字符串的表达式产生的
                    if (!originalVal.StartsWith("\"") || !originalVal.EndsWith("\""))
                    {
                        throw new InvalidOperationException($"无法将内容赋给 String 类型变量 @{varName}，字符串必须使用双引号包裹，例如：@A = \"内容\"");
                    }
                    break;
            }

            // 赋值
            variable.SetValue(finalVal);
            return true;
        }

        /// <summary>
        /// 检测表达式是否为赋值语句
        /// </summary>
        public bool IsAssignment(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return false;
            var assignPattern = @"^\s*@([\w\u4e00-\u9fff]+)\s*=\s*.+$";
            return Regex.IsMatch(expression, assignPattern);
        }

        /// <summary>
        /// 提取赋值语句右侧表达式
        /// </summary>
        public string GetAssignmentRightSide(string expression)
        {
            var assignPattern = @"^\s*@[\w\u4e00-\u9fff]+\s*=\s*(.+)$";
            var match = Regex.Match(expression, assignPattern);
            return match.Success ? match.Groups[1].Value : expression;
        }
    }
}
