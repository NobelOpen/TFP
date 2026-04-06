import re

file_path = r'c:\Users\31640\source\repos\TaskFlow\Resources\Strings.Designer.cs'

with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# 清理之前可能插入错误的行
lines = content.split('\n')
cleaned = []
skip_next = False
for line in lines:
    if 'HttpRequest' in line and 'ResourceManager' in line:
        continue  # 跳过之前错误插入的
    if 'HttpRequest' in line and '———' in line:
        continue  # 跳过注释行
    if 'AC_HttpStatusCode' in line and 'ResourceManager' in line:
        continue  # 跳过之前的
    cleaned.append(line)
content = '\n'.join(cleaned)

# 找到插入点
marker = 'Web Page Screenshot";'
idx = content.rfind(marker)
if idx < 0:
    print("FAIL: marker not found")
    exit(1)

eol = content.index('\n', idx)

insertion = '''
        // ——— HttpRequest 静默请求卡片 ———
        public static string TaskType_HttpRequest => ResourceManager.GetString("TaskType_HttpRequest", resourceCulture) ?? "HTTP Request (Silent)";
        public static string Prop_HttpUrl => ResourceManager.GetString("Prop_HttpUrl", resourceCulture) ?? "Request URL";
        public static string Prop_HttpMethod => ResourceManager.GetString("Prop_HttpMethod", resourceCulture) ?? "HTTP Method";
        public static string Prop_HttpHeaders => ResourceManager.GetString("Prop_HttpHeaders", resourceCulture) ?? "Custom Headers (Key: Value per line)";
        public static string Prop_HttpBody => ResourceManager.GetString("Prop_HttpBody", resourceCulture) ?? "Request Body (JSON)";
        public static string Prop_HttpTimeout => ResourceManager.GetString("Prop_HttpTimeout", resourceCulture) ?? "Timeout (ms)";
        public static string AC_HttpStatusCode => ResourceManager.GetString("AC_HttpStatusCode", resourceCulture) ?? "HTTP Status Code";
'''

# 将 \n 替换为 \r\n 以匹配 Windows 风格
insertion = insertion.replace('\n', '\r\n')

content = content[:eol+1] + insertion + content[eol+1:]

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)

print("OK: Inserted HttpRequest properties correctly")
