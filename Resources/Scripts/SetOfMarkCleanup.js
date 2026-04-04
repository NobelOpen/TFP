// ================================================================
// SetOfMarkCleanup.js — 标注清理脚本
// 功能：移除 SetOfMark.js 注入的视觉标注叠加层，
//       恢复页面原始外观。
// ================================================================
const overlay = document.getElementById('__som_overlay__');
if (overlay) overlay.remove();
return 'CLEANED';
