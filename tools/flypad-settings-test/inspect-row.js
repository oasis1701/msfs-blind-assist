// Inspect the divide-y-2 SettingItem rows of a fixture: for a row whose label
// matches the needle, print its child structure (tags/classes + control shapes).
//   node inspect-row.js flypad "Auto Brightness"
const { loadFixture } = require('./run');
const name = process.argv[2], needle = (process.argv[3] || '').toLowerCase();
const { window, A } = loadFixture(name);
const root = window.document.getElementById('MSFS_REACT_MOUNT');
const wrap = A.settingsContentRoot(root);
if (!wrap) { console.log('NO settingsContentRoot (bailed)'); process.exit(0); }
function cls(n){ return (n.className&&n.className.toString)?n.className.toString():''; }
function shape(n, d){
  const pad='  '.repeat(d);
  const t=A.directText(n).slice(0,30);
  const tag=n.tagName.toLowerCase();
  const role=n.getAttribute&&n.getAttribute('role')||'';
  const flags=[];
  if(tag==='input') flags.push('INPUT('+(n.getAttribute('type')||'text')+')');
  if(A.hasClassToken(n,'cursor-pointer')) flags.push('cursor-pointer');
  if(cls(n).indexOf('rounded-full')>=0) flags.push('rounded-full');
  if(cls(n).indexOf('rc-slider')>=0) flags.push('rc-slider');
  if(cls(n).indexOf('h-8')>=0) flags.push('h-8');
  if(role) flags.push('role='+role);
  return `${pad}${tag}.${cls(n).split(/\s+/).slice(0,3).join('.')} ${flags.join(',')} ${t?("'"+t+"'"):''}`;
}
function walk(n,d){ if(d>4)return; console.log(shape(n,d)); for(let i=0;i<n.children.length;i++) walk(n.children[i],d+1); }
const rows = wrap.children;
for (let i=0;i<rows.length;i++){
  const lbl = rows[i].firstElementChild ? A.directText(rows[i].firstElementChild) || (rows[i].textContent||'').replace(/\s+/g,' ').trim().slice(0,40) : '';
  if (!needle || (rows[i].textContent||'').toLowerCase().indexOf(needle)>=0) {
    console.log(`\n### ROW ${i}: ${(rows[i].textContent||'').replace(/\s+/g,' ').trim().slice(0,50)}`);
    walk(rows[i],0);
  }
}
