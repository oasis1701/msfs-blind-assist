const {get,evalOn}=require('./g1000-cdp.js');
async function findPage(match){
  const l=JSON.parse(await get('/pagelist.json'));
  const p=(l.pages||l).find(x=>(x.title||'').indexOf(match)>=0);
  if(!p) throw new Error('no page matching '+match);
  return {id:p.id,title:p.title,ws:'ws://127.0.0.1:19999/devtools/page/'+p.id};
}
async function run(match,expr,t){const p=await findPage(match);return {page:p.title,value:await evalOn(p.ws,expr,t||8000)}}
module.exports={findPage,run};
