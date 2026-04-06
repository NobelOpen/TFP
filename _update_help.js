const fs = require('fs');

// ==================== 中文帮助文档 ====================
const zhFile = 'docs/help_zh.html';
let zh = fs.readFileSync(zhFile, 'utf8');

// 正文：在 </div><!-- /container --> 前插入 HttpRequest 卡片
const zhContainerEnd = '  </div><!-- /container -->';
const zhNewCard = `    <div class="card-doc" id="HttpRequest">\r
      <h3>HTTP静默请求 <span class="badge badge-win">Web</span></h3>\r
      <p>在后台静默发送 HTTP 请求并获取响应内容。无需启动浏览器，适用于 API 调用、网页数据抓取、接口健康检查等场景。相比浏览器操作卡片，资源消耗极低且速度更快，但不支持 JavaScript 渲染。</p>\r
      <dl class="props">\r
        <dt>请求 URL</dt>\r
        <dd>目标地址，支持表达式。例如 <code>https://api.example.com/data</code> 或 <code>{{#2.text}}</code> 引用前序卡片输出的 URL。</dd>\r
        <dt>HTTP 方法</dt>\r
        <dd>请求方法，支持 <code>GET</code>（默认）和 <code>POST</code>。GET 用于获取数据，POST 用于提交数据。</dd>\r
        <dt>自定义请求头</dt>\r
        <dd>可选。每行一个 <code>Key: Value</code> 格式的请求头。常用于设置 <code>Authorization</code>、<code>Content-Type</code>、<code>User-Agent</code> 等。支持表达式。</dd>\r
        <dt>请求体 (JSON)</dt>\r
        <dd>可选。仅在 POST 请求时有效。填写 JSON 格式的请求体内容，如 <code>{"key": "value"}</code>。支持表达式。</dd>\r
        <dt>超时时间 (ms)</dt>\r
        <dd>请求超时的毫秒数，默认 <code>30000</code>（30秒）。超时后任务将标记为失败。</dd>\r
      </dl>\r
      <div class="output-section">\r
        <div class="output-title"> 输出</div>\r
        <div class="output-item"><code>#N.text</code> — 响应的文本内容（HTML 标签会被自动清除，仅保留纯文本）</div>\r
        <div class="output-item"><code>#N.状态码</code> 或 <code>#N.statusCode</code> — HTTP 响应状态码（如 200、404、500）</div>\r
      </div>\r
      <div class="example-section">\r
        <div class="example-title"> 使用示例</div>\r
        <div class="example-item"><strong>GET 请求：</strong>URL = <code>https://httpbin.org/get</code>，方法 = <code>GET</code> → 获取测试接口返回的数据</div>\r
        <div class="example-item"><strong>API 调用：</strong>URL = <code>https://api.example.com/users</code>，请求头 = <code>Authorization: Bearer sk-xxx</code></div>\r
        <div class="example-item"><strong>POST 提交：</strong>URL = <code>https://api.example.com/submit</code>，方法 = <code>POST</code>，请求体 = <code>{"name": "test"}</code></div>\r
        <div class="example-item"><strong>状态码判断：</strong>配合 If-Else 使用 <code>{{#N.状态码}} == 200</code> 判断请求是否成功</div>\r
        <div class="example-item"><strong>网页抓取：</strong>URL = <code>https://example.com</code> → 获取页面纯文本内容（不执行 JS，适合静态页面）</div>\r
      </div>\r
    </div>\r
\r
`;

const zhIdx = zh.indexOf(zhContainerEnd);
if (zhIdx < 0) {
  console.log('FAIL: ZH container end not found');
  process.exit(1);
}
zh = zh.slice(0, zhIdx) + zhNewCard + zh.slice(zhIdx);
fs.writeFileSync(zhFile, zh);
console.log('OK: Updated help_zh.html body');

// ==================== 英文帮助文档 ====================
const enFile = 'docs/help_en.html';
let en = fs.readFileSync(enFile, 'utf8');

const enContainerEnd = '</div><!-- /container -->';
const enNewCard = `    <div class="card-doc" id="HttpRequest">\r
      <h3>HTTP Silent Request <span class="badge badge-win">Web</span></h3>\r
      <p>Sends HTTP requests silently in the background and retrieves the response content. No browser instance required — ideal for API calls, web scraping, health checks, and data retrieval tasks. Compared to browser-based cards, this consumes minimal resources and executes much faster, but does not support JavaScript rendering.</p>\r
      <dl class="props">\r
        <dt>Request URL</dt>\r
        <dd>Target URL, supports expressions. E.g. <code>https://api.example.com/data</code> or <code>{{#2.text}}</code> to reference a URL from a preceding card's output.</dd>\r
        <dt>HTTP Method</dt>\r
        <dd>Request method: <code>GET</code> (default) or <code>POST</code>. GET for data retrieval, POST for data submission.</dd>\r
        <dt>Custom Headers</dt>\r
        <dd>Optional. One <code>Key: Value</code> header per line. Commonly used for <code>Authorization</code>, <code>Content-Type</code>, <code>User-Agent</code>, etc. Supports expressions.</dd>\r
        <dt>Request Body (JSON)</dt>\r
        <dd>Optional. Only effective for POST requests. Enter JSON-formatted body content, e.g. <code>{"key": "value"}</code>. Supports expressions.</dd>\r
        <dt>Timeout (ms)</dt>\r
        <dd>Request timeout in milliseconds, default <code>30000</code> (30 seconds). The task is marked as failed upon timeout.</dd>\r
      </dl>\r
      <div class="output-section">\r
        <div class="output-title"> Output</div>\r
        <div class="output-item"><code>#N.text</code> — Response text content (HTML tags are automatically stripped, returning plain text only)</div>\r
        <div class="output-item"><code>#N.statusCode</code> — HTTP response status code (e.g. 200, 404, 500)</div>\r
      </div>\r
      <div class="example-section">\r
        <div class="example-title"> Examples</div>\r
        <div class="example-item"><strong>GET Request:</strong> URL = <code>https://httpbin.org/get</code>, Method = <code>GET</code> → Fetches test API response data</div>\r
        <div class="example-item"><strong>API Call:</strong> URL = <code>https://api.example.com/users</code>, Headers = <code>Authorization: Bearer sk-xxx</code></div>\r
        <div class="example-item"><strong>POST Submit:</strong> URL = <code>https://api.example.com/submit</code>, Method = <code>POST</code>, Body = <code>{"name": "test"}</code></div>\r
        <div class="example-item"><strong>Status Check:</strong> Use with If-Else: <code>{{#N.statusCode}} == 200</code> to verify request success</div>\r
        <div class="example-item"><strong>Web Scraping:</strong> URL = <code>https://example.com</code> → Retrieves plain text page content (no JS execution, ideal for static pages)</div>\r
      </div>\r
    </div>\r
\r
`;

const enIdx = en.indexOf(enContainerEnd);
if (enIdx < 0) {
  console.log('FAIL: EN container end not found');
  process.exit(1);
}
en = en.slice(0, enIdx) + enNewCard + en.slice(enIdx);
fs.writeFileSync(enFile, en);
console.log('OK: Updated help_en.html body');
