/**
 * TaskFlow 本地 API 代理
 * 用法：node local_proxy.mjs [端口] [目标域名]
 * 示例：node local_proxy.mjs 9876 api.312800.xyz
 */

import http from 'node:http';
import https from 'node:https';

const PORT = parseInt(process.argv[2]) || 9876;
const TARGET_HOST = process.argv[3] || 'api.312800.xyz';
const PARENT_PID = parseInt(process.argv[4]); // 从参数获取父进程 PID

// 自动殉葬机制：如果传递了父进程 PID，则每秒检查一次父进程是否存活
if (PARENT_PID && !isNaN(PARENT_PID)) {
  setInterval(() => {
    try {
      // process.kill(pid, 0) 不会发送真实信号，如果进程不存在会抛出 ESRCH 错误
      process.kill(PARENT_PID, 0);
    } catch (e) {
      if (e.code === 'ESRCH') {
        console.log(`[Auto-Exit] TaskFlow process (PID: ${PARENT_PID}) is dead. Exiting proxy...`);
        process.exit(0);
      }
    }
  }, 2000).unref(); // unref 防止此定时器阻止正常的进程退出
}

const server = http.createServer((req, res) => {
  let body = '';
  req.on('data', chunk => body += chunk);
  req.on('end', () => {
    const targetPath = req.url;
    console.log(`[${new Date().toLocaleTimeString()}] ${req.method} ${targetPath}`);

    // 只保留必要的请求头，不转发客户端的原始头部
    const headers = {
      'Host': TARGET_HOST,
      'Content-Type': 'application/json',
      'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
      'Accept': 'application/json, text/event-stream',
      // SSE 必须禁用压缩，否则响应会被 gzip 缓冲区拦截导致无法流式逐字输出
      'Accept-Encoding': 'identity',
    };

    // 转发 Authorization 头
    if (req.headers['authorization']) {
      headers['Authorization'] = req.headers['authorization'];
    }

    if (body) {
      headers['Content-Length'] = Buffer.byteLength(body);
    }

    const options = {
      hostname: TARGET_HOST,
      port: 443,
      path: targetPath,
      method: req.method,
      headers: headers,
      // 自定义 TLS 算法套件打乱 Node.js 默认的 ClientHello 指纹，防止 Cloudflare 直接 RST 连接
      ciphers: 'TLS_AES_128_GCM_SHA256:TLS_AES_256_GCM_SHA384:TLS_CHACHA20_POLY1305_SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-ECDSA-AES128-GCM-SHA256',
      minVersion: 'TLSv1.2'
    };

    const proxyReq = https.request(options, (proxyRes) => {
      console.log(`[${new Date().toLocaleTimeString()}] <- ${proxyRes.statusCode} ${proxyRes.headers['content-type'] || ''}`);
      
      const respHeaders = { ...proxyRes.headers };
      // 只删除 content-encoding（因为我们已要求 identity 不压缩）
      // 保留 transfer-encoding 以支持 chunked 流式传输
      delete respHeaders['content-encoding'];
      
      res.writeHead(proxyRes.statusCode, respHeaders);
      // 禁用 Nagle 算法，减少小数据包的合并延迟（对 SSE 逐事件推送至关重要）
      res.socket?.setNoDelay(true);

      // 手动逐块转发，每收到一个 chunk 立即写入并 flush，不使用 pipe（pipe 有内部缓冲）
      proxyRes.on('data', (chunk) => {
        res.write(chunk);
      });
      proxyRes.on('end', () => {
        res.end();
      });
    });

    proxyReq.on('error', (err) => {
      console.error(`[${new Date().toLocaleTimeString()}] Error: ${err.message}`);
      res.writeHead(502, { 'Content-Type': 'text/plain' });
      res.end(`Proxy Error: ${err.message}`);
    });

    if (body) proxyReq.write(body);
    proxyReq.end();
  });
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`PROXY_READY ${PORT} ${TARGET_HOST}`);
});
