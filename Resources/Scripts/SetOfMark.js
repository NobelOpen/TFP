// ================================================================
// SetOfMark.js — 视觉标注注入脚本
// 功能：为页面上所有可交互元素注入带编号的红色标签，
//       并返回精确到亚像素的坐标映射表 JSON。
// 注意：此脚本被引擎包裹在 () => { ... } 中执行，
//       因此必须使用 return 返回映射表。
// ================================================================

// 1. 清理可能残留的旧标注
const oldOverlay = document.getElementById('__som_overlay__');
if (oldOverlay) oldOverlay.remove();

// 2. 收集所有候选交互元素（分优先级）
const interactiveTags = new Set(['A', 'BUTTON', 'SELECT', 'TEXTAREA']);
const inputTypes = new Set(['submit', 'button', 'reset', 'checkbox', 'radio', 'file', 'text', 'password', 'email', 'search', 'url', 'tel', 'number', 'date', 'range', 'color']);
const interactiveRoles = new Set(['button', 'link', 'menuitem', 'option', 'tab', 'checkbox', 'radio', 'switch', 'textbox', 'combobox', 'searchbox', 'slider']);

// 判断元素是否可见
function isVisible(el) {
    if (el.offsetWidth === 0 && el.offsetHeight === 0) return false;
    const style = window.getComputedStyle(el);
    if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') return false;
    if (el.getAttribute('aria-hidden') === 'true') return false;
    return true;
}

// 判断元素是否为可交互元素
function isInteractive(el) {
    const tag = el.tagName;
    // 第一优先级：原生交互标签
    if (interactiveTags.has(tag)) return true;
    // INPUT 需要判断 type
    if (tag === 'INPUT') {
        const type = (el.getAttribute('type') || 'text').toLowerCase();
        return inputTypes.has(type);
    }
    // 第二优先级：ARIA role 标注的交互元素
    const role = (el.getAttribute('role') || '').toLowerCase();
    if (interactiveRoles.has(role)) return true;
    // 第三优先级：有 onclick 事件或 tabindex 的元素
    if (el.hasAttribute('onclick') || el.hasAttribute('ng-click') || el.hasAttribute('@click') || el.hasAttribute('v-on:click')) return true;
    if (el.hasAttribute('tabindex') && el.getAttribute('tabindex') !== '-1') return true;
    // 第四优先级：CSS cursor: pointer 的元素（仅限 div/span 等非语义标签）
    if (tag === 'DIV' || tag === 'SPAN' || tag === 'LI') {
        const cursor = window.getComputedStyle(el).cursor;
        if (cursor === 'pointer') return true;
    }
    return false;
}

// 获取元素的可读文本（截断到合理长度）
function getLabel(el) {
    // 优先取 aria-label
    const ariaLabel = el.getAttribute('aria-label');
    if (ariaLabel) return ariaLabel.trim().substring(0, 50);
    // 其次取 innerText（只取直接文本，避免嵌套节点的文本污染）
    const text = (el.innerText || el.textContent || el.value || el.placeholder || '').replace(/\s+/g, ' ').trim();
    return text.substring(0, 50);
}

// 获取元素的语义角色标识
function getRole(el) {
    const role = el.getAttribute('role');
    if (role) return role;
    const tag = el.tagName.toLowerCase();
    if (tag === 'a') return 'link';
    if (tag === 'button') return 'button';
    if (tag === 'input') return el.getAttribute('type') || 'text';
    if (tag === 'select') return 'select';
    if (tag === 'textarea') return 'textarea';
    return tag;
}

// 判断元素或其祖先是否为 position: fixed（弹窗/对话框常见）
function isFixedPosition(el) {
    let current = el;
    while (current && current !== document.documentElement) {
        const pos = window.getComputedStyle(current).position;
        if (pos === 'fixed') return true;
        current = current.parentElement;
    }
    return false;
}

// 3. 遍历并收集
const allElements = document.querySelectorAll('*');
const candidates = [];
const seenBounds = []; // 用于去重

for (const el of allElements) {
    if (!isInteractive(el) || !isVisible(el)) continue;

    const rect = el.getBoundingClientRect();
    // 排除极小的元素（宽或高小于 5 像素）
    if (rect.width < 5 || rect.height < 5) continue;

    // 检测是否为 fixed 定位（弹窗、对话框、浮层等）
    const fixed = isFixedPosition(el);

    // 计算坐标：
    // - 普通元素：全页面绝对坐标 = viewportPos + scroll
    // - fixed 元素：纯视口坐标（不加 scroll，因为 fixed 不随滚动移动）
    const pageX = fixed ? rect.left : rect.left + window.scrollX;
    const pageY = fixed ? rect.top : rect.top + window.scrollY;
    const cx = pageX + rect.width / 2;
    const cy = pageY + rect.height / 2;

    // 去重：如果已有完全重叠的元素（父子 BoundingBox 一致），跳过外层
    const boundsKey = `${Math.round(pageX)},${Math.round(pageY)},${Math.round(rect.width)},${Math.round(rect.height)}`;
    if (seenBounds.includes(boundsKey)) continue;
    seenBounds.push(boundsKey);

    candidates.push({
        el: el,
        cx: Math.round(cx * 10) / 10,
        cy: Math.round(cy * 10) / 10,
        left: Math.round(pageX),
        top: Math.round(pageY),
        w: Math.round(rect.width),
        h: Math.round(rect.height),
        text: getLabel(el),
        role: getRole(el),
        tag: el.tagName,
        fixed: fixed
    });
}

// 4. 注入视觉标签覆盖层
const overlay = document.createElement('div');
overlay.id = '__som_overlay__';
overlay.style.cssText = 'position:absolute;top:0;left:0;width:0;height:0;z-index:2147483647;pointer-events:none;';
document.body.appendChild(overlay);

const mappings = [];

for (let i = 0; i < candidates.length; i++) {
    const c = candidates[i];
    const id = i + 1;

    // 创建标签元素
    // fixed 元素的标签也用 fixed 定位，确保在截图中标签与元素视觉位置对齐
    const label = document.createElement('div');
    const posType = c.fixed ? 'fixed' : 'absolute';
    label.style.cssText = `
        position:${posType};
        left:${c.fixed ? c.left : c.left}px;
        top:${c.fixed ? c.top : c.top}px;
        min-width:18px;
        height:16px;
        line-height:16px;
        padding:0 3px;
        background:rgba(220,38,38,0.85);
        color:#fff;
        font-size:11px;
        font-weight:700;
        font-family:Arial,sans-serif;
        text-align:center;
        border-radius:3px;
        pointer-events:none;
        box-shadow:0 1px 3px rgba(0,0,0,0.3);
        white-space:nowrap;
        z-index:2147483647;
    `;
    label.textContent = id;
    // fixed 标签直接挂到 body，不放在 overlay 里（overlay 是 absolute 定位会影响 fixed 标签）
    if (c.fixed) {
        document.body.appendChild(label);
        label.classList.add('__som_fixed_label__');
    } else {
        overlay.appendChild(label);
    }

    // 构建映射条目
    mappings.push({
        id: id,
        cx: c.cx,
        cy: c.cy,
        text: c.text,
        role: c.role,
        tag: c.tag,
        fixed: c.fixed || false
    });
}

return JSON.stringify(mappings);
