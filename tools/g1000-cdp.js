// Minimal Coherent GT (Chromium 49) CDP client - raw WebSocket, no npm deps.
const net=require('net'),http=require('http'),crypto=require('crypto');
function get(path){return new Promise((res,rej)=>{http.get({host:'127.0.0.1',port:19999,path},r=>{let d='';r.on('data',c=>d+=c);r.on('end',()=>res(d))}).on('error',rej)})}
function frame(payload){ // client->server, masked text frame
  const b=Buffer.from(payload,'utf8'),m=crypto.randomBytes(4);let h;
  if(b.length<126)h=Buffer.from([0x81,0x80|b.length]);
  else if(b.length<65536){h=Buffer.alloc(4);h[0]=0x81;h[1]=0xFE;h.writeUInt16BE(b.length,2);h=Buffer.concat([h,Buffer.alloc(0)]);h=Buffer.from([0x81,0xFE,(b.length>>8)&255,b.length&255]);}
  else {h=Buffer.alloc(10);h[0]=0x81;h[1]=0xFF;h.writeUInt32BE(0,2);h.writeUInt32BE(b.length,6);}
  const p=Buffer.alloc(b.length);for(let i=0;i<b.length;i++)p[i]=b[i]^m[i%4];
  return Buffer.concat([h,m,p]);
}
function parse(buf){ // server->client frames (unmasked); returns [msgs, rest]
  const out=[];let o=0;
  while(o+2<=buf.length){
    const b1=buf[o],b2=buf[o+1],op=b1&0x0f,masked=b2&0x80;let len=b2&0x7f,p=o+2;
    if(len===126){if(p+2>buf.length)break;len=buf.readUInt16BE(p);p+=2}
    else if(len===127){if(p+8>buf.length)break;len=Number(buf.readBigUInt64BE(p));p+=8}
    let mk=null;if(masked){if(p+4>buf.length)break;mk=buf.slice(p,p+4);p+=4}
    if(p+len>buf.length)break;
    let d=buf.slice(p,p+len);
    if(mk){const x=Buffer.alloc(len);for(let i=0;i<len;i++)x[i]=d[i]^mk[i%4];d=x}
    if(op===1)out.push(d.toString('utf8'));
    o=p+len;
  }
  return [out,buf.slice(o)];
}
async function evalOn(wsUrl,expression,timeoutMs=8000){
  const u=new URL(wsUrl);
  return new Promise((res,rej)=>{
    const sock=net.connect(Number(u.port||19999),u.hostname,()=>{
      const key=crypto.randomBytes(16).toString('base64');
      sock.write(`GET ${u.pathname}${u.search} HTTP/1.1\r\nHost: ${u.host}\r\n`+
        `Upgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: ${key}\r\nSec-WebSocket-Version: 13\r\n\r\n`);
    });
    let hs=false,buf=Buffer.alloc(0);
    const t=setTimeout(()=>{sock.destroy();rej(new Error('timeout'))},timeoutMs);
    sock.on('data',c=>{
      buf=Buffer.concat([buf,c]);
      if(!hs){const i=buf.indexOf('\r\n\r\n');if(i<0)return;
        const head=buf.slice(0,i).toString();
        if(!/101/.test(head)){clearTimeout(t);sock.destroy();return rej(new Error('no upgrade: '+head.split('\r\n')[0]))}
        buf=buf.slice(i+4);hs=true;
        sock.write(frame(JSON.stringify({id:1,method:'Runtime.evaluate',params:{expression,returnByValue:true}})));
      }
      const [msgs,rest]=parse(buf);buf=rest;
      for(const m of msgs){let o;try{o=JSON.parse(m)}catch(e){continue}
        if(o.id===1){clearTimeout(t);sock.destroy();
          const r=o.result&&o.result.result;
          return res(r&&('value' in r)?r.value:JSON.stringify(o.result||o));}}
    });
    sock.on('error',e=>{clearTimeout(t);rej(e)});
  });
}
module.exports={get,evalOn};
if(require.main===module){(async()=>{
  const raw=await get('/pagelist.json');
  let pages;try{pages=JSON.parse(raw)}catch(e){console.log('RAW:',raw.slice(0,500));process.exit(1)}
  const list=pages.pages||pages;
  list.forEach((p,i)=>console.log(i,'|',p.title||p.description||'?','|',p.webSocketDebuggerUrl||p.url||''));
})()}
