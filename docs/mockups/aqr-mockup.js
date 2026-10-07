/* AQR UI mockup — synthetic data + rendering. No external dependencies. */
(() => {
'use strict';

// ---------------------------------------------------------------- utils
function rng(seed){return function(){seed|=0;seed=seed+0x6D2B79F5|0;let t=Math.imul(seed^seed>>>15,1|seed);t=t+Math.imul(t^t>>>7,61|t)^t;return((t^t>>>14)>>>0)/4294967296;};}
const R = rng(20261007);
const pick = a => a[Math.floor(R()*a.length)];
const $ = id => document.getElementById(id);
const fmt = n => n.toLocaleString('en-US');
const pct = (u,l) => l>0 ? 100*u/l : 0;
const esc = s => String(s).replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
const fillClass = p => p>=100?'f-over':p>=90?'f-crit':p>=75?'f-warn':'f-ok';
const badge = p => p>=100?'<span class="badge over">at limit</span>':p>=90?`<span class="badge crit">${p.toFixed(0)}%</span>`:p>=75?`<span class="badge warnb">${p.toFixed(0)}%</span>`:`<span class="badge ok">${p.toFixed(0)}%</span>`;

// ---------------------------------------------------------------- catalog (S4 Resource SKUs + S7 catalog file)
const LEARN = 'https://learn.microsoft.com/azure/virtual-machines/sizes/overview';
const CAP_RESTRICT = 'https://learn.microsoft.com/azure/virtual-machines/sizes/lifecycle/retirements-and-capacity-restrictions';
function F(id, series, cat, cpu, arch, gen, feats, life, skus, acc){
  return {id, series, cat, cpu, arch, gen, feats, life, skus, acc: acc||null};
}
const sk = (fmtS, sizes) => sizes.map(n => [fmtS.replace('{n}', n), n]);
const FAM = [
  F('standardDSv5Family','Dsv5','GeneralPurpose','Intel','x64','v5',['s'],'Current',sk('Standard_D{n}s_v5',[2,4,8,16,32,48,64,96])),
  F('standardDDSv5Family','Ddsv5','GeneralPurpose','Intel','x64','v5',['d','s'],'Current',sk('Standard_D{n}ds_v5',[2,4,8,16,32,48,64,96])),
  F('standardDASv5Family','Dasv5','GeneralPurpose','AMD','x64','v5',['s'],'Current',sk('Standard_D{n}as_v5',[2,4,8,16,32,48,64,96])),
  F('standardDSv6Family','Dsv6','GeneralPurpose','Intel','x64','v6',['s'],'Current',sk('Standard_D{n}s_v6',[2,4,8,16,32,48,64,96,128])),
  F('standardDASv6Family','Dasv6','GeneralPurpose','AMD','x64','v6',['s'],'Current',sk('Standard_D{n}as_v6',[2,4,8,16,32,48,64,96])),
  F('standardDPSv6Family','Dpsv6','GeneralPurpose','Microsoft','Arm64','v6',['s'],'Current',sk('Standard_D{n}ps_v6',[2,4,8,16,32,48,64,96])),
  F('standardDPSv5Family','Dpsv5','GeneralPurpose','Ampere','Arm64','v5',['s'],'Current',sk('Standard_D{n}ps_v5',[2,4,8,16,32,48,64])),
  F('standardDCASv5Family','DCasv5','GeneralPurpose','AMD','x64','v5',['s','confidential'],'Current',sk('Standard_DC{n}as_v5',[2,4,8,16,32,48,64,96])),
  F('standardBsv2Family','Bsv2','GeneralPurpose','Intel','x64','v2',['s','burstable'],'Current',sk('Standard_B{n}s_v2',[2,4,8,16,32])),
  F('standardDSv3Family','Dsv3','GeneralPurpose','Intel','x64','v3',['s'],'Capacity-restricted',sk('Standard_D{n}s_v3',[2,4,8,16,32,48,64])),
  F('standardBSFamily','Bs','GeneralPurpose','Intel','x64','v1',['s','burstable'],'Capacity-restricted',sk('Standard_B{n}ms',[1,2,4,8,12,16,20])),
  F('standardESv5Family','Esv5','MemoryOptimized','Intel','x64','v5',['s'],'Current',sk('Standard_E{n}s_v5',[2,4,8,16,20,32,48,64,96,104])),
  F('standardEDSv5Family','Edsv5','MemoryOptimized','Intel','x64','v5',['d','s'],'Current',sk('Standard_E{n}ds_v5',[2,4,8,16,20,32,48,64,96,104])),
  F('standardEASv5Family','Easv5','MemoryOptimized','AMD','x64','v5',['s'],'Current',sk('Standard_E{n}as_v5',[2,4,8,16,20,32,48,64,96])),
  F('standardESv6Family','Esv6','MemoryOptimized','Intel','x64','v6',['s'],'Current',sk('Standard_E{n}s_v6',[2,4,8,16,20,32,48,64,96,128])),
  F('standardESv3Family','Esv3','MemoryOptimized','Intel','x64','v3',['s'],'Capacity-restricted',sk('Standard_E{n}s_v3',[2,4,8,16,20,32,48,64])),
  F('standardMSFamily','M','MemoryOptimized','Intel','x64','v1',['s'],'Current',sk('Standard_M{n}s',[8,16,32,64,128])),
  F('standardMSMediumMemoryv3Family','Mv3 medium memory','MemoryOptimized','Intel','x64','v3',['s'],'Current',sk('Standard_M{n}bs_v3',[48,64,96,128,176])),
  F('standardFSv2Family','Fsv2','ComputeOptimized','Intel','x64','v2',['s'],'Capacity-restricted',sk('Standard_F{n}s_v2',[2,4,8,16,32,48,64,72])),
  F('standardFASv6Family','Fasv6','ComputeOptimized','AMD','x64','v6',['s'],'Current',sk('Standard_F{n}as_v6',[2,4,8,16,32,48,64])),
  F('standardLSv3Family','Lsv3','StorageOptimized','Intel','x64','v3',['d','s'],'Current',sk('Standard_L{n}s_v3',[8,16,32,48,64,80])),
  F('standardLASv3Family','Lasv3','StorageOptimized','AMD','x64','v3',['d','s'],'Current',sk('Standard_L{n}as_v3',[8,16,32,48,64,80])),
  F('standardHBv4Family','HBv4','HighPerformanceCompute','AMD','x64','v4',['s','rdma'],'Current',[['Standard_HB176rs_v4',176],['Standard_HB176-96rs_v4',96],['Standard_HB176-48rs_v4',48]]),
  F('standardNCADSH100v5Family','NCads H100 v5','GpuAccelerated','AMD','x64','v5',['d','s'],'Current',[['Standard_NC40ads_H100_v5',40],['Standard_NC80adis_H100_v5',80]],{type:'GPU',vendor:'Nvidia',model:'H100'}),
  F('standardNDSH100v5Family','ND H100 v5','GpuAccelerated','Intel','x64','v5',['d','s','rdma'],'Current',[['Standard_ND96isr_H100_v5',96]],{type:'GPU',vendor:'Nvidia',model:'H100'}),
  F('standardNDAMSv4_A100Family','NDm A100 v4','GpuAccelerated','AMD','x64','v4',['d','s','rdma'],'Current',[['Standard_ND96amsr_A100_v4',96]],{type:'GPU',vendor:'Nvidia',model:'A100'}),
  F('standardNCASv3_T4Family','NCasT4_v3','GpuAccelerated','AMD','x64','v3',['s'],'Current',sk('Standard_NC{n}as_T4_v3',[4,8,16,64]),{type:'GPU',vendor:'Nvidia',model:'T4'}),
  F('standardNVADSA10v5Family','NVadsA10 v5','GpuAccelerated','AMD','x64','v5',['d','s'],'Current',sk('Standard_NV{n}ads_A10_v5',[6,12,18,36,72]),{type:'GPU',vendor:'Nvidia',model:'A10'}),
];
const FAMBY = Object.fromEntries(FAM.map(f=>[f.id,f]));
const CATS = ['GeneralPurpose','ComputeOptimized','MemoryOptimized','StorageOptimized','GpuAccelerated','HighPerformanceCompute'];
const CATLBL = {GeneralPurpose:'General purpose',ComputeOptimized:'Compute optimized',MemoryOptimized:'Memory optimized',StorageOptimized:'Storage optimized',GpuAccelerated:'GPU accelerated',HighPerformanceCompute:'HPC'};
const CPUCLS = {Intel:'c-intel',AMD:'c-amd',Microsoft:'c-msft',Ampere:'c-ampere'};
const CPULBL = {Intel:'Intel',AMD:'AMD',Microsoft:'Microsoft Cobalt',Ampere:'Ampere'};
const FEATLBL = {s:'Premium SSD',d:'Local disk',rdma:'RDMA',confidential:'Confidential',burstable:'Burstable'};

// ---------------------------------------------------------------- regions (S5)
const REG = [
  {n:'eastus',geo:'Americas',az:true},{n:'eastus2',geo:'Americas',az:true},{n:'centralus',geo:'Americas',az:true},
  {n:'westus3',geo:'Americas',az:true},{n:'westcentralus',geo:'Americas',az:false},
  {n:'northeurope',geo:'Europe',az:true},{n:'westeurope',geo:'Europe',az:true},{n:'swedencentral',geo:'Europe',az:true},{n:'uksouth',geo:'Europe',az:true},
  {n:'southeastasia',geo:'Asia Pacific',az:true},{n:'japaneast',geo:'Asia Pacific',az:true},
];
const REGBY = Object.fromEntries(REG.map(r=>[r.n,r]));

// ---------------------------------------------------------------- subscriptions (S6) + groups (S3)
const GROUPS = [
  {name:'qg-prod-compute', mg:'mg-platform', color:'#2563eb'},
  {name:'qg-ai-gpu', mg:'mg-ai', color:'#7c3aed'},
];
const SUBS = [
  {name:'sub-prod-core-01', group:'qg-prod-compute', mg:'mg-platform/mg-prod', regions:['eastus2','centralus','westeurope','northeurope'], fams:['standardDSv5Family','standardDDSv5Family','standardDASv5Family','standardDSv6Family','standardDASv6Family','standardESv5Family','standardEASv5Family','standardDSv3Family','standardFASv6Family','standardDCASv5Family']},
  {name:'sub-prod-data-02', group:'qg-prod-compute', mg:'mg-platform/mg-prod', regions:['eastus2','westus3'], fams:['standardESv5Family','standardEDSv5Family','standardESv6Family','standardLSv3Family','standardLASv3Family','standardMSFamily','standardESv3Family','standardDSv5Family']},
  {name:'sub-sap-prod-01', group:'qg-prod-compute', mg:'mg-platform/mg-sap', regions:['eastus2','westeurope'], fams:['standardMSFamily','standardMSMediumMemoryv3Family','standardESv5Family','standardEDSv5Family','standardDSv5Family']},
  {name:'sub-dr-01', group:'qg-prod-compute', mg:'mg-platform/mg-prod', regions:['centralus','northeurope'], fams:['standardDSv5Family','standardESv5Family','standardDASv5Family','standardFSv2Family']},
  {name:'sub-ai-train-01', group:'qg-ai-gpu', mg:'mg-ai', regions:['eastus2','swedencentral','westus3'], fams:['standardNDSH100v5Family','standardNDAMSv4_A100Family','standardNCADSH100v5Family','standardHBv4Family','standardDSv5Family']},
  {name:'sub-ai-infer-02', group:'qg-ai-gpu', mg:'mg-ai', regions:['eastus2','eastus','swedencentral','japaneast'], fams:['standardNCASv3_T4Family','standardNVADSA10v5Family','standardNCADSH100v5Family','standardDASv5Family','standardDSv6Family']},
  {name:'sub-dev-shared-01', group:null, mg:'mg-nonprod', regions:['eastus','westcentralus','uksouth'], fams:['standardBsv2Family','standardBSFamily','standardDSv5Family','standardDPSv6Family','standardDPSv5Family','standardFSv2Family','standardDSv3Family']},
  {name:'sub-sandbox-01', group:null, mg:'mg-nonprod/mg-sandbox', regions:['southeastasia','eastus'], fams:['standardDSv5Family','standardDPSv6Family','standardBsv2Family','standardFASv6Family','standardEASv5Family']},
];
SUBS.forEach((s,i)=>{ s.id = ['a1f3','b27c','c90e','d4b1','e6a2','f81d','0c5e','19aa'][i]+'…'+(1000+i*137).toString(16); s.idx=i; });
const SUBBY = Object.fromEntries(SUBS.map(s=>[s.name,s]));

// logical → physical zone mapping per subscription (S5). Differs per sub, by design.
const PERMS = [[1,2,3],[2,3,1],[3,1,2],[1,3,2],[2,1,3],[3,2,1]];
const ZMAP = {}; // ZMAP[sub][region] = {1:'eastus2-az2',...}
SUBS.forEach(s=>{ ZMAP[s.name]={}; REG.forEach(r=>{ if(!r.az) return; const p = PERMS[(s.idx*3 + r.n.length) % PERMS.length]; ZMAP[s.name][r.n] = {1:`${r.n}-az${p[0]}`,2:`${r.n}-az${p[1]}`,3:`${r.n}-az${p[2]}`}; }); });
const phys2log = (sub,reg,ph) => { const m = ZMAP[sub][reg]; if(!m) return null; return +Object.keys(m).find(k=>m[k]===ph); };

// SKU offered zones (physical) per region×family, and restrictions per sub×region×sku (S4)
const OFFER = {}; // `${reg}|${fam}` -> {sku: [physical zones]}
REG.forEach(r=>FAM.forEach(f=>{
  const o={}; f.skus.forEach(([name,n],i)=>{
    if(!r.az){o[name]=[];return;}
    let z=[1,2,3];
    if(f.acc && R()<.55) z = pick([[1,2],[1,3],[2,3],[2]]);          // GPUs often not in all zones
    else if(i===f.skus.length-1 && R()<.35) z = pick([[1,2],[2,3]]);   // largest size sometimes partial
    o[name]=z.map(x=>`${r.n}-az${x}`);
  }); OFFER[`${r.n}|${f.id}`]=o;
}));
const RESTR = {}; // `${sub}|${reg}|${fam}` -> {region:true} or {zones:{sku:[physical]}}
function restrictionFor(sub, reg, fam){
  const k=`${sub}|${reg}|${fam}`; if(RESTR[k]) return RESTR[k];
  const f=FAMBY[fam], r=REGBY[reg]; let res={region:false,zones:{}};
  const roll=R();
  if(f.acc && roll<.12) res.region=true;
  else if(f.life==='Capacity-restricted' && roll<.25) res.region=true;
  else if(r.az){
    const z = roll<.10 ? 'all' : roll<.32 ? 'one' : null;
    if(z){ const ph = pick([1,2,3]); f.skus.forEach(([name])=>{ const off=OFFER[`${reg}|${fam}`][name]; res.zones[name]= z==='all'?off.slice(): off.filter(p=>p.endsWith('az'+ph)); }); }
  }
  // hand-placed stories for the mockup
  if(sub==='sub-ai-train-01' && reg==='eastus2' && fam==='standardNDSH100v5Family'){ res={region:false,zones:{}}; OFFER[`${reg}|${fam}`]['Standard_ND96isr_H100_v5']=['eastus2-az1','eastus2-az2','eastus2-az3']; res.zones['Standard_ND96isr_H100_v5']=['eastus2-az2','eastus2-az3']; }
  if(sub==='sub-ai-infer-02' && reg==='eastus2' && fam==='standardNCADSH100v5Family'){ res={region:true,zones:{}}; }
  if(sub==='sub-prod-core-01' && reg==='eastus2' && fam==='standardDSv5Family'){ res={region:false,zones:{}}; }
  return RESTR[k]=res;
}
// zone summary for a family row in one subscription
function zoneInfo(sub, reg, fam){
  const r=REGBY[reg]; if(!r.az) return {status:'Regional', zones:[]};
  const res=restrictionFor(sub,reg,fam); if(res.region) return {status:'RegionBlocked', zones:[]};
  const off=OFFER[`${reg}|${fam}`]; const skus=Object.keys(off);
  const zones=[1,2,3].map(L=>{
    const ph=ZMAP[sub][reg][L]; let offered=0, open=0;
    skus.forEach(s=>{ if(off[s].includes(ph)){ offered++; if(!(res.zones[s]||[]).includes(ph)) open++; } });
    return {L, ph, offered, open, total:skus.length, state: offered===0?'none': open===0?'restr': open<offered?'part':'open'};
  });
  const offeredZ=zones.filter(z=>z.state!=='none'), openZ=zones.filter(z=>z.state==='open'||z.state==='part');
  const status = openZ.length===0 ? 'NoZones' : (openZ.length<offeredZ.length || zones.some(z=>z.state==='part')) ? 'PartialZones' : 'AllZones';
  return {status, zones};
}

// ---------------------------------------------------------------- quota rows (S1)
const LIMS=[10,20,50,100,150,200,350,500,800,1000,1500,2000];
const ROWS=[]; // family rows
const REGROWS=[]; // cores + lowPriorityCores
SUBS.forEach(s=>s.regions.forEach(reg=>{
  let coreUse=0, coreLim=0;
  s.fams.forEach(fam=>{
    const f=FAMBY[fam];
    const story=(s.name==='sub-prod-core-01'&&reg==='eastus2'&&fam==='standardDSv5Family')||(s.name==='sub-ai-train-01'&&reg==='eastus2'&&/NDSH100|NCADSH100/.test(fam))||(s.name==='sub-ai-infer-02'&&reg==='eastus2'&&fam==='standardNCADSH100v5Family');
    if(R()<.18 && !story) return;
    let lim = f.acc ? pick([0,96,192,384,768,1152]) : pick(LIMS);
    if(f.id==='standardHBv4Family') lim=pick([352,704,1408]);
    if(lim===0) lim=96;
    const u = R(); let use = u<.15?0 : u<.22 ? lim : Math.round(lim*(.1+R()*.78));
    if(f.acc){ const step=f.skus[0][1]; use=Math.min(lim, Math.round(use/step)*step); }
    if(s.name==='sub-ai-train-01'&&reg==='eastus2'&&fam==='standardNDSH100v5Family'){lim=1152;use=960;}
    if(s.name==='sub-ai-infer-02'&&reg==='eastus2'&&fam==='standardNCADSH100v5Family'){lim=320;use=0;}
    if(s.name==='sub-prod-core-01'&&reg==='eastus2'&&fam==='standardDSv5Family'){lim=2000;use=1968;}
    const zi=zoneInfo(s.name,reg,fam);
    ROWS.push({sub:s.name, group:s.group, reg, geo:REGBY[reg].geo, fam, f, use, lim, avail:Math.max(0,lim-use), util:pct(use,lim), zi});
    coreUse+=use; coreLim+=lim;
  });
  const cl = Math.max(coreLim, Math.ceil(coreLim*(.55+R()*.4)/50)*50);
  REGROWS.push({sub:s.name, group:s.group, reg, geo:REGBY[reg].geo, kind:'cores', name:'Total Regional vCPUs', use:coreUse, lim:Math.max(cl,coreUse), util:pct(coreUse,Math.max(cl,coreUse))});
  const sl=pick([100,200,500,1000]), su=Math.round(sl*R()*.7);
  REGROWS.push({sub:s.name, group:s.group, reg, geo:REGBY[reg].geo, kind:'lowPriorityCores', name:'Total Regional Low-priority vCPUs', use:su, lim:sl, util:pct(su,sl)});
}));

// group quotas (S3): per group × region × family
const GQ=[]; // {group, reg, fam, alloc:{sub:n}, unalloc, limit, usage}
GROUPS.forEach(g=>{
  const members=SUBS.filter(s=>s.group===g.name);
  const keys=new Set(); ROWS.filter(r=>r.group===g.name).forEach(r=>keys.add(r.reg+'|'+r.fam));
  [...keys].forEach(k=>{
    const [reg,fam]=k.split('|'); const alloc={}; let usage=0;
    members.forEach(m=>{ const row=ROWS.find(r=>r.sub===m.name&&r.reg===reg&&r.fam===fam); if(row){ alloc[m.name]=row.lim; usage+=row.use; } });
    const unalloc = FAMBY[fam].acc ? pick([0,0,0,96,192]) : pick([0,0,0,50,100,200]);
    const allocated=Object.values(alloc).reduce((a,b)=>a+b,0);
    GQ.push({group:g.name, reg, fam, alloc, allocated, unalloc, limit:allocated+unalloc, usage});
  });
});

// ---------------------------------------------------------------- shared render helpers
let ZMODE='logical';
function famCell(f, compact){
  const chips=[`<span class="chip ${CPUCLS[f.cpu]}">${CPULBL[f.cpu]}</span>`];
  if(f.arch==='Arm64') chips.push('<span class="chip c-arm">Arm64</span>');
  if(f.acc) chips.push(`<span class="chip c-gpu">${f.acc.vendor} ${f.acc.model}</span>`);
  if(f.feats.includes('rdma')) chips.push('<span class="chip c-feat">RDMA</span>');
  if(f.feats.includes('confidential')) chips.push('<span class="chip c-feat">Confidential</span>');
  if(f.life==='Capacity-restricted') chips.push(`<a class="chip c-restricted" href="${CAP_RESTRICT}" target="_blank" title="Capacity growth restriction — Microsoft Learn">growth-restricted</a>`);
  return `<div class="fam">${esc(f.series)}${compact?'':`<small>${esc(f.id)}</small>`}</div><div>${chips.join('')}</div>`;
}
function meterCell(use, lim){
  const p=pct(use,lim);
  return `<div class="mcell"><div class="t"><b>${fmt(use)}</b> / ${fmt(lim)} vCPU</div><div class="meter"><i class="${fillClass(p)}" style="width:${Math.min(100,p)}%"></i></div></div>`;
}
function zonePills(zi, mode){
  if(zi.status==='Regional') return '<span class="regional" title="Region has no availability zones — regional (non-zonal) deployment only">regional only</span>';
  if(zi.status==='RegionBlocked') return '<span class="blocked" title="restrictions[type=Location] NotAvailableForSubscription">region blocked</span>';
  let zs=zi.zones.slice();
  if(mode==='physical') zs.sort((a,b)=>a.ph.localeCompare(b.ph));
  const pills=zs.map(z=>{
    const lbl = mode==='physical' ? 'az'+z.ph.split('-az')[1] : z.L;
    const cls = z.state==='open'?'open':z.state==='part'?'open':z.state==='restr'?'restr':'none';
    const tip = z.state==='none'?'not offered': `${z.open}/${z.offered} SKUs open` + (mode==='logical'?` · logical ${z.L} = ${z.ph}`:` · logical zone ${z.L} in this sub`);
    const style = z.state==='part' ? ' style="background:#fff7e6;border-color:#fcd9a8;color:#b45309"' : '';
    return `<span class="z ${cls}${mode==='physical'?' wide':''}"${style} title="${tip}">${lbl}${z.state==='part'?'½':''}</span>`;
  }).join('');
  const lbl={AllZones:'all zones',PartialZones:'partial',NoZones:'no zones'}[zi.status];
  return `<span class="zones">${pills}</span><span class="zstat">${lbl}</span>`;
}
function donut(data, size){
  const tot=data.reduce((a,d)=>a+d.v,0)||1; let a0=-Math.PI/2; const r=size/2-8, cx=size/2, cy=size/2, w=22;
  const segs=data.map(d=>{ const a1=a0+2*Math.PI*d.v/tot; const large=a1-a0>Math.PI?1:0;
    const p=`M${cx+r*Math.cos(a0)} ${cy+r*Math.sin(a0)} A${r} ${r} 0 ${large} 1 ${cx+r*Math.cos(a1)} ${cy+r*Math.sin(a1)}`; a0=a1;
    return `<path d="${p}" stroke="${d.c}" stroke-width="${w}" fill="none"/>`; }).join('');
  return `<svg width="${size}" height="${size}" viewBox="0 0 ${size} ${size}">${segs}<text x="${cx}" y="${cy-2}" text-anchor="middle" font-size="19" font-weight="800" fill="#141a22">${fmt(tot)}</text><text x="${cx}" y="${cy+15}" text-anchor="middle" font-size="11" fill="#66707d">vCPUs in use</text></svg>`;
}
const CPUCOLOR={Intel:'#2563eb',AMD:'#e0533d',Microsoft:'#0f9d58',Ampere:'#7c3aed'};

// ---------------------------------------------------------------- OVERVIEW
function renderOverview(){
  const cores=REGROWS.filter(r=>r.kind==='cores'), spot=REGROWS.filter(r=>r.kind==='lowPriorityCores');
  const cU=cores.reduce((a,r)=>a+r.use,0), cL=cores.reduce((a,r)=>a+r.lim,0);
  const hot=ROWS.filter(r=>r.util>=80), full=ROWS.filter(r=>r.util>=100);
  const unalloc=GQ.reduce((a,g)=>a+g.unalloc,0);
  const blocked=ROWS.filter(r=>r.avail>0&&(r.zi.status==='RegionBlocked'||r.zi.status==='NoZones'));
  const partial=ROWS.filter(r=>r.avail>0&&r.zi.status==='PartialZones');
  const sU=spot.reduce((a,r)=>a+r.use,0), sL=spot.reduce((a,r)=>a+r.lim,0);
  $('ov-kpis').innerHTML=[
    ['b1','🧮','Regional vCPU quota',fmt(cL),`${fmt(cU)} in use · ${pct(cU,cL).toFixed(0)}%`,''],
    ['b5','🔥','Family quotas ≥ 80%',hot.length,`${full.length} at limit · of ${ROWS.length} family quotas`,'bad'],
    ['b2','👥','Quota group pool',fmt(unalloc),'unallocated vCPUs · 2 groups · not deployable until allocated',''],
    ['b4','🚧','Quota not deployable',blocked.length,`${fmt(blocked.reduce((a,r)=>a+r.avail,0))} vCPUs available but region/zone blocked · +${partial.length} partial-zone`,'bad'],
    ['b3','⚡','Spot / low-priority',`${pct(sU,sL).toFixed(0)}%`,`${fmt(sU)} / ${fmt(sL)} vCPUs`,''],
  ].map(([b,ic,l,v,n,c])=>`<div class="kpi ${b}"><div class="ic">${ic}</div><div class="label">${l}</div><div class="val">${v}</div><div class="note ${c}">${n}</div></div>`).join('');

  const top=[...ROWS].sort((a,b)=>b.util-a.util||b.lim-a.lim).slice(0,12);
  $('ov-top').innerHTML=`<thead><tr><th>VM family</th><th>Subscription</th><th>Region</th><th>Quota (regional)</th><th class="num">Util</th><th class="num">Available</th><th>Zones (logical)</th></tr></thead><tbody>`+
    top.map(r=>`<tr><td>${famCell(r.f,true)}</td><td>${esc(r.sub)}${r.group?`<div><span class="chip c-group">${r.group}</span></div>`:''}</td><td>${r.reg}</td><td>${meterCell(r.use,r.lim)}</td><td class="num">${badge(r.util)}</td><td class="num">${fmt(r.avail)}</td><td>${zonePills(r.zi,'logical')}</td></tr>`).join('')+'</tbody>';

  // heatmap
  const regs=REG.filter(r=>ROWS.some(x=>x.reg===r.n));
  const cell=(reg,cat)=>{ const rs=ROWS.filter(x=>x.reg===reg&&x.f.cat===cat); if(!rs.length) return '<td><span class="muted">—</span></td>';
    const u=rs.reduce((a,x)=>a+x.use,0), l=rs.reduce((a,x)=>a+x.lim,0), p=pct(u,l);
    const bg = p>=90?'#fde8e8':p>=75?'#fdeede':p>=50?'#fff7e6':p>=25?'#e6f4ea':'#f1f8f3'; const fg=p>=90?'#b42318':p>=75?'#b45309':'#14532d';
    return `<td><div class="hc" style="background:${bg};color:${fg}" title="${fmt(u)} / ${fmt(l)} vCPU">${p.toFixed(0)}%</div></td>`; };
  $('ov-heat').innerHTML=`<thead><tr><th style="text-align:left">Region</th>${CATS.map(c=>`<th>${CATLBL[c]}</th>`).join('')}</tr></thead><tbody>`+
    regs.map(r=>`<tr><td class="rl">${r.n}${r.az?'':' <span class="regional">no AZ</span>'}</td>${CATS.map(c=>cell(r.n,c)).join('')}</tr>`).join('')+'</tbody>';

  // donut
  const byCpu=['Intel','AMD','Microsoft','Ampere'].map(c=>({k:c,v:ROWS.filter(r=>r.f.cpu===c).reduce((a,r)=>a+r.use,0),c:CPUCOLOR[c]}));
  $('ov-donut').innerHTML=donut(byCpu,170);
  $('ov-donut-lg').innerHTML=byCpu.map(d=>`<div class="row"><span class="sw" style="background:${d.c}"></span>${CPULBL[d.k]}<span class="amt">${fmt(d.v)}</span></div>`).join('')+
    `<div class="note" style="margin-top:4px">GPU families counted by host CPU. Vendor from <code>vm-families.json</code>.</div>`;

  // blocked
  const bl=[...blocked,...partial].sort((a,b)=>b.avail-a.avail).slice(0,7);
  $('ov-blocked').innerHTML=bl.map(r=>`<div style="display:flex;justify-content:space-between;gap:10px;padding:7px 0;border-bottom:1px solid #f1f4f9">
    <div><b>${esc(r.f.series)}</b> · ${r.reg}<div class="note">${esc(r.sub)} · ${fmt(r.avail)} vCPU available</div></div><div style="text-align:right">${zonePills(r.zi,'logical')}</div></div>`).join('');

  // regional totals
  const byReg=regs.map(r=>{const rs=cores.filter(x=>x.reg===r.n);return {r:r.n,u:rs.reduce((a,x)=>a+x.use,0),l:rs.reduce((a,x)=>a+x.lim,0)};}).sort((a,b)=>b.l-a.l).slice(0,8);
  $('ov-regional').innerHTML=byReg.map(x=>{const p=pct(x.u,x.l);return `<div class="hbar"><span class="lbl">${x.r}</span><span class="trk"><i class="${fillClass(p)}" style="width:${Math.min(100,p)}%"></i></span><span class="v">${fmt(x.u)}/${fmt(x.l)}</span></div>`;}).join('');

  // lifecycle
  const lc=FAM.filter(f=>f.life==='Capacity-restricted').map(f=>{const rs=ROWS.filter(r=>r.fam===f.id);return {f,u:rs.reduce((a,r)=>a+r.use,0),l:rs.reduce((a,r)=>a+r.lim,0),n:rs.length};}).filter(x=>x.n);
  $('ov-lifecycle').innerHTML=lc.map(x=>`<div class="hbar"><span class="lbl"><b>${x.f.series}</b> <span class="note">${x.n} quotas</span></span><span class="trk"><i class="f-warn" style="width:${Math.min(100,pct(x.u,x.l))}%"></i></span><span class="v">${fmt(x.u)}/${fmt(x.l)}</span></div>`).join('')+
    `<div class="note" style="margin-top:8px">Additional quota for these series isn't approved, and new subscriptions can't deploy them (<a href="${CAP_RESTRICT}" target="_blank">Microsoft Learn</a>).</div>`;
}

// ---------------------------------------------------------------- EXPLORER
const FILTERS=[
  {k:'kind',t:'Quota type',single:true,opts:[['Family','VM family'],['cores','Total regional vCPUs'],['lowPriorityCores','Spot / low-priority']],def:['Family']},
  {k:'sub',t:'Subscription',opts:SUBS.map(s=>[s.name,s.name.replace('sub-','')])},
  {k:'group',t:'Quota group',opts:[...GROUPS.map(g=>[g.name,g.name]),['(none)','not in a group']]},
  {k:'geo',t:'Geography',opts:['Americas','Europe','Asia Pacific'].map(x=>[x,x])},
  {k:'reg',t:'Region',opts:REG.map(r=>[r.n,r.n])},
  {k:'zstat',t:'Zone access',fam:true,opts:[['AllZones','All zones open'],['PartialZones','Partial zones'],['NoZones','No zones open'],['RegionBlocked','Region blocked'],['Regional','Regional (no AZ)']]},
  {k:'zopen',t:'Must be open in zone',fam:true,dyn:true},
  {k:'cat',t:'VM category',fam:true,opts:CATS.map(c=>[c,CATLBL[c]])},
  {k:'cpu',t:'CPU manufacturer',fam:true,opts:[['Intel','Intel'],['AMD','AMD'],['Microsoft','Microsoft Cobalt'],['Ampere','Ampere']]},
  {k:'arch',t:'Architecture',fam:true,opts:[['x64','x64'],['Arm64','Arm64']]},
  {k:'acc',t:'Accelerator',fam:true,opts:[['none','None'],['GPU','GPU'],['FPGA','FPGA']]},
  {k:'gpu',t:'GPU model',fam:true,opts:[['H100','NVIDIA H100'],['A100','NVIDIA A100'],['A10','NVIDIA A10'],['T4','NVIDIA T4'],['MI300X','AMD MI300X']]},
  {k:'gen',t:'Generation',fam:true,opts:['v1','v2','v3','v4','v5','v6'].map(x=>[x,x])},
  {k:'feat',t:'Features (all of)',fam:true,opts:Object.entries(FEATLBL)},
  {k:'life',t:'Lifecycle',fam:true,opts:[['Current','Current'],['Capacity-restricted','Capacity-restricted']]},
];
const FS={}; FILTERS.forEach(f=>FS[f.k]=new Set(f.def||[]));
let FUTIL=0, FHEAD=0, FTEXT='', SORT={k:'util',d:-1};

function buildFilters(){
  const h=[`<div class="grp"><div class="gt">Search</div><input type="search" id="fx-text" placeholder="family, SKU series, subscription…"></div>`];
  FILTERS.forEach(f=>{
    if(f.dyn){ h.push(`<div class="grp" data-fam="1"><div class="gt">${f.t} <span class="hint" id="fx-zlbl"></span></div><div class="checks" id="fx-${f.k}"></div></div>`); return; }
    h.push(`<div class="grp" ${f.fam?'data-fam="1"':''}><div class="gt">${f.t}${f.single?'':` <a data-clear="${f.k}">clear</a>`}</div><div class="checks">${f.opts.map(([v,l])=>`<label data-k="${f.k}" data-v="${esc(v)}" class="${FS[f.k].has(v)?'on':''}"><input type="checkbox">${esc(l)}</label>`).join('')}</div></div>`);
  });
  h.push(`<div class="grp"><div class="gt">Utilization ≥ <b id="fx-ul">0%</b></div><input type="range" id="fx-util" min="0" max="100" step="5" value="0"></div>`);
  h.push(`<div class="grp"><div class="gt">Available vCPUs ≥</div><input type="search" id="fx-head" placeholder="e.g. 96" inputmode="numeric"></div>`);
  $('ex-filters').innerHTML=h.join('');
  $('ex-filters').addEventListener('click',e=>{
    const lab=e.target.closest('label[data-k]'); const clr=e.target.closest('a[data-clear]');
    if(lab){ e.preventDefault(); const k=lab.dataset.k,v=lab.dataset.v,f=FILTERS.find(x=>x.k===k);
      if(f&&f.single){ FS[k]=new Set([v]); } else { FS[k].has(v)?FS[k].delete(v):FS[k].add(v); }
      if(k==='zopen'){ /* handled below */ }
      renderExplorer(); }
    if(clr){ FS[clr.dataset.clear].clear(); renderExplorer(); }
  });
  $('fx-text').addEventListener('input',e=>{FTEXT=e.target.value.toLowerCase();renderExplorer();});
  $('fx-util').addEventListener('input',e=>{FUTIL=+e.target.value;$('fx-ul').textContent=FUTIL+'%';renderExplorer();});
  $('fx-head').addEventListener('input',e=>{FHEAD=+e.target.value||0;renderExplorer();});
}
function zopenOptions(){ return ZMODE==='logical' ? [['1','Zone 1'],['2','Zone 2'],['3','Zone 3']] : [['az1','az1'],['az2','az2'],['az3','az3']]; }

function explorerRows(){
  const kind=[...FS.kind][0]||'Family'; const isFam=kind==='Family';
  const has=(k,v)=>FS[k].size===0||FS[k].has(v);
  let rows = isFam ? ROWS : REGROWS.filter(r=>r.kind===kind);
  rows=rows.filter(r=>has('sub',r.sub)&&has('group',r.group||'(none)')&&has('geo',r.geo)&&has('reg',r.reg)&&r.util>=FUTIL&&(r.lim-r.use)>=FHEAD);
  if(isFam){
    rows=rows.filter(r=>{const f=r.f;
      if(!has('zstat',r.zi.status)||!has('cat',f.cat)||!has('cpu',f.cpu)||!has('arch',f.arch)||!has('life',f.life)||!has('gen',f.gen)) return false;
      if(FS.acc.size && !FS.acc.has(f.acc?f.acc.type:'none')) return false;
      if(FS.gpu.size && !(f.acc&&FS.gpu.has(f.acc.model))) return false;
      if(FS.feat.size && ![...FS.feat].every(x=>f.feats.includes(x))) return false;
      if(FS.zopen.size){ if(!r.zi.zones.length) return false;
        const ok=[...FS.zopen].every(z=>{ const zz = ZMODE==='logical' ? r.zi.zones.find(q=>String(q.L)===z) : r.zi.zones.find(q=>q.ph.endsWith('-'+z)); return zz&&(zz.state==='open'||zz.state==='part'); });
        if(!ok) return false; }
      if(FTEXT && !(f.series+' '+f.id+' '+r.sub+' '+r.reg+' '+f.skus.map(s=>s[0]).join(' ')).toLowerCase().includes(FTEXT)) return false;
      return true;});
  } else if(FTEXT) rows=rows.filter(r=>(r.sub+' '+r.reg).toLowerCase().includes(FTEXT));
  const key={util:r=>r.util,avail:r=>r.lim-r.use,lim:r=>r.lim,sub:r=>r.sub,reg:r=>r.reg,fam:r=>r.f?r.f.series:r.name}[SORT.k];
  rows=[...rows].sort((a,b)=>{const x=key(a),y=key(b);return (x>y?1:x<y?-1:0)*SORT.d;});
  return {rows,isFam,kind};
}
function renderExplorer(){
  document.querySelectorAll('#ex-filters label[data-k]').forEach(l=>l.classList.toggle('on',FS[l.dataset.k].has(l.dataset.v)));
  const isFamNow=([...FS.kind][0]||'Family')==='Family';
  document.querySelectorAll('#ex-filters .grp[data-fam]').forEach(g=>g.style.opacity=isFamNow?1:.4);
  // dynamic zone options
  const zo=zopenOptions(); [...FS.zopen].forEach(v=>{ if(!zo.some(o=>o[0]===v)) FS.zopen.delete(v); });
  $('fx-zopen').innerHTML=zo.map(([v,l])=>`<label data-k="zopen" data-v="${v}" class="${FS.zopen.has(v)?'on':''}"><input type="checkbox">${l}</label>`).join('');
  $('fx-zlbl').textContent = ZMODE==='logical'?'(logical, per sub)':'(physical)';

  const {rows,isFam,kind}=explorerRows();
  const u=rows.reduce((a,r)=>a+r.use,0), l=rows.reduce((a,r)=>a+r.lim,0);
  $('ex-summary').innerHTML=`<b>${fmt(rows.length)}</b> quotas · <b>${fmt(u)}</b> / ${fmt(l)} vCPU in use (${pct(u,l).toFixed(0)}%) · <b>${fmt(l-u)}</b> available` +
    (isFam?` · <span style="color:#b42318;font-weight:700">${rows.filter(r=>r.avail>0&&['NoZones','RegionBlocked'].includes(r.zi.status)).length} not deployable</span>`:'');
  // zone-mapping warning
  const subsInView=new Set(rows.map(r=>r.sub)), regsInView=new Set(rows.map(r=>r.reg));
  let differs=0; regsInView.forEach(reg=>{ if(!REGBY[reg].az) return; const sigs=new Set([...subsInView].filter(s=>rows.some(r=>r.sub===s&&r.reg===reg)).map(s=>JSON.stringify(ZMAP[s][reg]))); if(sigs.size>1) differs++; });
  $('ex-zonewarn').innerHTML = (isFam && ZMODE==='logical' && differs) ? `⚠ Logical zone numbers differ between subscriptions in <b>${differs}</b> region(s) in view. Switch to <b>Physical</b> to compare zones across subscriptions.` : (isFam&&ZMODE==='physical'?'Showing physical zones (datacenter IDs) — comparable across subscriptions.':'');
  // api line
  const qs=[]; FILTERS.forEach(f=>{ if(FS[f.k].size && !(f.k==='kind'&&FS.kind.has('Family'))) qs.push(`${f.k}=${[...FS[f.k]].map(encodeURIComponent).join(',')}`); });
  if(FUTIL) qs.push('minUtil='+FUTIL); if(FHEAD) qs.push('minAvailable='+FHEAD); if(FTEXT) qs.push('q='+encodeURIComponent(FTEXT)); qs.push('zoneMode='+ZMODE);
  $('ex-api').textContent='GET /api/v1/quota/subscriptions?'+qs.join('&');

  const th=(k,l,num)=>`<th class="sort${num?' num':''}" data-s="${k}">${l}${SORT.k===k?(SORT.d<0?' ▼':' ▲'):''}</th>`;
  const head=`<thead><tr>${th('fam',isFam?'VM family':'Quota')}${th('sub','Subscription')}${th('reg','Region')}${th('lim','Quota (regional)')}${th('util','Util',1)}${th('avail','Available',1)}${isFam?`<th>Zones (${ZMODE})</th>`:''}</tr></thead>`;
  const body=rows.slice(0,400).map(r=>`<tr><td>${isFam?famCell(r.f):`<div class="fam">${r.name}<small>${r.kind}</small></div>`}</td><td>${esc(r.sub)}${r.group?`<div><span class="chip c-group">${r.group}</span></div>`:''}</td><td>${r.reg}${REGBY[r.reg].az?'':'<div><span class="regional">no AZ</span></div>'}</td><td>${meterCell(r.use,r.lim)}</td><td class="num">${badge(r.util)}</td><td class="num">${fmt(r.lim-r.use)}</td>${isFam?`<td>${zonePills(r.zi,ZMODE)}</td>`:''}</tr>`).join('');
  $('ex-table').innerHTML=head+'<tbody>'+(body||'<tr><td colspan="7" class="note" style="padding:24px;text-align:center">No quotas match these filters.</td></tr>')+'</tbody>';
}
$('ex-table').addEventListener('click',e=>{const t=e.target.closest('th[data-s]'); if(!t) return; const k=t.dataset.s; SORT = SORT.k===k?{k,d:-SORT.d}:{k,d:k==='sub'||k==='reg'||k==='fam'?1:-1}; renderExplorer();});
$('ex-csv').addEventListener('click',()=>{
  const {rows,isFam}=explorerRows();
  const hdr=['subscription','quotaGroup','region','quota','series','category','cpuManufacturer','architecture','accelerator','usage','limit','available','utilPct','zoneStatus','openZonesLogical','openZonesPhysical'];
  const lines=[hdr.join(',')].concat(rows.map(r=>[r.sub,r.group||'',r.reg,isFam?r.fam:r.kind,isFam?r.f.series:'',isFam?r.f.cat:'',isFam?r.f.cpu:'',isFam?r.f.arch:'',isFam&&r.f.acc?r.f.acc.vendor+' '+r.f.acc.model:'',r.use,r.lim,r.lim-r.use,r.util.toFixed(1),isFam?r.zi.status:'',
    isFam?r.zi.zones.filter(z=>z.state==='open'||z.state==='part').map(z=>z.L).join(' '):'', isFam?r.zi.zones.filter(z=>z.state==='open'||z.state==='part').map(z=>z.ph).join(' '):''].map(v=>`"${String(v).replace(/"/g,'""')}"`).join(',')));
  const a=document.createElement('a'); a.href=URL.createObjectURL(new Blob([lines.join('\n')],{type:'text/csv'})); a.download='aqr-quota-export.csv'; a.click();
});

// ---------------------------------------------------------------- GROUPS
function renderGroups(){
  const g=$('g-sel').value, reg=$('g-region').value;
  const members=SUBS.filter(s=>s.group===g);
  const rows=GQ.filter(x=>x.group===g&&(reg==='*'||x.reg===reg));
  const L=rows.reduce((a,x)=>a+x.limit,0), A=rows.reduce((a,x)=>a+x.allocated,0), U=rows.reduce((a,x)=>a+x.unalloc,0), US=rows.reduce((a,x)=>a+x.usage,0);
  $('g-kpis').innerHTML=[['b1','🏦','Group limit',fmt(L),`${rows.length} family × region limits`],['b2','📤','Allocated to subscriptions',fmt(A),`${pct(A,L).toFixed(0)}% of group limit`],
    ['b4','🧊','Unallocated pool',fmt(U),'availableLimit · not deployable until allocated'],['b3','⚙️','Usage across members',fmt(US),`${pct(US,A).toFixed(0)}% of allocated`]]
    .map(([b,ic,l,v,n])=>`<div class="kpi ${b}"><div class="ic">${ic}</div><div class="label">${l}</div><div class="val">${v}</div><div class="note">${n}</div></div>`).join('');
  const gm=GROUPS.find(x=>x.name===g); $('g-mg').textContent=`Management group: ${gm.mg}`;
  const colors=['#2563eb','#0d9488','#d9822b','#7c3aed','#e0533d'];
  const col=Object.fromEntries(members.map((m,i)=>[m.name,colors[i%colors.length]]));
  $('g-hint').textContent = reg==='*'?'all regions':reg;
  $('g-table').innerHTML=`<thead><tr><th>VM family</th><th>Region</th><th class="num">Group limit</th><th class="num">Allocated</th><th class="num">Unallocated</th><th class="num">Usage</th><th>Allocation by subscription</th></tr></thead><tbody>`+
    rows.sort((a,b)=>b.limit-a.limit).map(x=>{const f=FAMBY[x.fam];
      const segs=Object.entries(x.alloc).map(([s,n])=>`<i style="width:${100*n/x.limit}%;background:${col[s]}" title="${s}: ${fmt(n)}"></i>`).join('')+(x.unalloc?`<i class="unalloc" style="width:${100*x.unalloc/x.limit}%" title="unallocated: ${fmt(x.unalloc)}"></i>`:'');
      return `<tr><td>${famCell(f,true)}</td><td>${x.reg}</td><td class="num">${fmt(x.limit)}</td><td class="num">${fmt(x.allocated)}</td><td class="num">${x.unalloc?fmt(x.unalloc):'<span class="muted">0</span>'}</td><td class="num">${fmt(x.usage)}</td><td><div class="stack">${segs}</div></td></tr>`;}).join('')+'</tbody>';
  $('g-legend').innerHTML=members.map(m=>`<span class="hbar" style="margin:0"><span class="sw" style="width:12px;height:12px;border-radius:3px;background:${col[m.name]}"></span>${m.name}</span>`).join('')+'<span class="hbar" style="margin:0"><span class="stack" style="min-width:24px;width:24px;height:12px"><i class="unalloc" style="width:100%"></i></span>unallocated</span>';
  $('g-members').innerHTML=`<thead><tr><th>Subscription</th><th class="num">Allocated</th><th class="num">Usage</th><th>Blocked fams</th></tr></thead><tbody>`+members.map(m=>{
    const a=rows.reduce((s,x)=>s+(x.alloc[m.name]||0),0); const us=ROWS.filter(r=>r.sub===m.name&&(reg==='*'||r.reg===reg));
    const b=us.filter(r=>['NoZones','RegionBlocked'].includes(r.zi.status)).length;
    return `<tr><td><b>${m.name}</b><div class="note">${m.id} · ${m.mg}</div></td><td class="num">${fmt(a)}</td><td class="num">${fmt(us.reduce((s,r)=>s+r.use,0))}</td><td>${b?`<span class="badge crit">${b}</span>`:'<span class="badge ok">0</span>'}</td></tr>`;}).join('')+'</tbody>';
  const regs = reg==='*' ? [...new Set(rows.map(x=>x.reg))].filter(r=>REGBY[r].az).slice(0,3) : (REGBY[reg].az?[reg]:[]);
  $('g-zmap').innerHTML = regs.length ? regs.map(r=>{
    const sigs=new Set(members.map(m=>JSON.stringify(ZMAP[m.name][r])));
    return `<div style="margin-bottom:10px"><b>${r}</b> ${sigs.size>1?'<span class="badge warnb">mappings differ</span>':'<span class="badge ok">same</span>'}<table style="margin-top:4px"><tbody>`+members.map(m=>`<tr><td style="padding:4px 6px">${m.name}</td>${[1,2,3].map(L=>`<td style="padding:4px 6px"><span class="z open" style="min-width:20px">${L}</span> → <code>${ZMAP[m.name][r][L].split('-')[1]}</code></td>`).join('')}</tr>`).join('')+'</tbody></table></div>';
  }).join('') : '<div class="note">Selected region has no availability zones.</div>';
}

// ---------------------------------------------------------------- ZONES
function renderZones(){
  const reg=$('z-region').value; const fam=$('z-family').value; const f=FAMBY[fam]; const r=REGBY[reg];
  const subs=SUBS.filter(s=>ROWS.some(x=>x.sub===s.name&&x.reg===reg&&x.fam===fam));
  $('z-hint').textContent=`${f.series} · ${reg} · ${subs.length} subscription(s)`;
  const off=OFFER[`${reg}|${fam}`];
  let head, body='';
  if(!r.az){ head='<thead><tr><th>SKU</th><th class="num">vCPU</th><th>Subscription</th><th>Access</th></tr></thead>'; }
  else if(ZMODE==='logical'){ head='<thead><tr><th>SKU</th><th class="num">vCPU</th><th>Subscription</th><th>Zone 1</th><th>Zone 2</th><th>Zone 3</th><th>Status</th></tr></thead>'; }
  else { head=`<thead><tr><th>SKU</th><th class="num">vCPU</th><th>Subscription</th><th>${reg}-az1</th><th>${reg}-az2</th><th>${reg}-az3</th><th>Status</th></tr></thead>`; }
  f.skus.forEach(([sku,n])=>subs.forEach((s,i)=>{
    const res=restrictionFor(s.name,reg,fam);
    let cells='', status='';
    if(!r.az){ cells=`<td>${res.region?'<span class="blocked">region blocked</span>':'<span class="regional">regional only</span>'}</td>`; }
    else if(res.region){ cells='<td colspan="3"><span class="blocked">region blocked · NotAvailableForSubscription</span></td>'; status='<span class="badge over">blocked</span>'; }
    else {
      let open=0, offered=0;
      cells=[1,2,3].map(z=>{ const ph = ZMODE==='logical'? ZMAP[s.name][reg][z] : `${reg}-az${z}`; const L = ZMODE==='logical'? z : phys2log(s.name,reg,ph);
        const isOff=off[sku].includes(ph), isRes=(res.zones[sku]||[]).includes(ph); if(isOff) offered++; if(isOff&&!isRes) open++;
        const cls=!isOff?'none':isRes?'restr':'open'; const sub = ZMODE==='logical'?`<span class="note"> ${ph.split('-')[1]}</span>`:`<span class="note"> L${L}</span>`;
        return `<td><span class="z ${cls}">${!isOff?'—':isRes?'✕':'✓'}</span>${sub}</td>`; }).join('');
      status = open===offered ? '<span class="badge ok">all offered open</span>' : open===0 ? '<span class="badge crit">no zones</span>' : `<span class="badge warnb">${open}/${offered} zones</span>`;
    }
    body+=`<tr${i===0?' style="border-top:2px solid #e7ebf2"':''}><td>${i===0?`<b>${sku}</b>`:''}</td><td class="num">${i===0?n:''}</td><td>${s.name}</td>${cells}${r.az?`<td>${status}</td>`:''}</tr>`;
  }));
  $('z-table').innerHTML=head+'<tbody>'+(body||'<tr><td colspan="7" class="note" style="padding:20px">No subscription holds quota for this family in this region.</td></tr>')+'</tbody>';
  const q=ROWS.filter(x=>x.reg===reg&&x.fam===fam);
  $('z-quota').innerHTML='<thead><tr><th>Subscription</th><th>Quota (regional)</th><th>Zones</th></tr></thead><tbody>'+q.map(x=>`<tr><td>${x.sub}</td><td>${meterCell(x.use,x.lim)}</td><td>${zonePills(x.zi,ZMODE)}</td></tr>`).join('')+'</tbody>';
  $('z-map').innerHTML = r.az ? '<table><thead><tr><th>Subscription</th><th>Logical 1</th><th>Logical 2</th><th>Logical 3</th></tr></thead><tbody>'+subs.map(s=>`<tr><td>${s.name}</td>${[1,2,3].map(L=>`<td><code>${ZMAP[s.name][reg][L].split('-')[1]}</code></td>`).join('')}</tr>`).join('')+'</tbody></table><div class="note" style="margin-top:8px">Source: <code>GET /subscriptions/{id}/locations</code> → <code>availabilityZoneMappings</code>.</div>' : '<div class="note">This region has no availability zones; only regional (non-zonal) deployments are possible.</div>';
}
function zoneSelectors(){
  const regs=[...new Set(ROWS.map(r=>r.reg))]; $('z-region').innerHTML=regs.map(r=>`<option ${r==='eastus2'?'selected':''}>${r}</option>`).join('');
  const fillFam=()=>{ const reg=$('z-region').value; const fams=[...new Set(ROWS.filter(r=>r.reg===reg).map(r=>r.fam))];
    const cur=$('z-family').value; $('z-family').innerHTML=fams.map(f=>`<option value="${f}">${FAMBY[f].series}</option>`).join('');
    $('z-family').value = fams.includes(cur)?cur:(fams.includes('standardNCADSH100v5Family')?'standardNCADSH100v5Family':fams[0]); };
  fillFam(); $('z-region').addEventListener('change',()=>{fillFam();renderZones();}); $('z-family').addEventListener('change',renderZones);
}

// ---------------------------------------------------------------- TRENDS
let TDAYS=30;
const TKEYS=[...ROWS].sort((a,b)=>b.util-a.util).slice(0,10);
function series(row){ const r2=rng(row.lim*7+row.use+row.reg.length); const n=90; const use=[], lim=[]; let L=Math.round(row.lim*.6/10)*10||row.lim, u=row.use*.55;
  const bump=[55,78]; for(let i=0;i<n;i++){ if(i===bump[0]) L=Math.round(row.lim*.8); if(i===bump[1]) L=row.lim; u=Math.min(L, Math.max(0,u+(row.use-u)*.04+(r2()-.45)*row.lim*.03)); if(i===n-1) u=row.use; use.push(Math.round(u)); lim.push(L);} return {use,lim}; }
function renderTrends(){
  const row=TKEYS[+$('t-key').value]; const s=series(row); const n=TDAYS; const U=s.use.slice(-n), L=s.lim.slice(-n);
  const alloc = null;
  $('t-title').textContent=`${row.f.series} · ${row.reg} · ${row.sub}`;
  const W=760,H=300,P=40, max=Math.max(...L)*1.1; const x=i=>P+i*(W-P-30)/(n-1), y=v=>H-P+ -v*(H-2*P)/max;
  const path=a=>a.map((v,i)=>`${i?'L':'M'}${x(i).toFixed(1)} ${y(v).toFixed(1)}`).join(' ');
  const grid=[0,.25,.5,.75,1].map(t=>{const v=Math.round(max*t);return `<line x1="${P}" x2="${W-10}" y1="${y(v)}" y2="${y(v)}" stroke="#eef1f6"/><text x="${P-6}" y="${y(v)+4}" text-anchor="end" font-size="10" fill="#66707d">${fmt(v)}</text>`;}).join('');
  const area=`${path(U)} L${x(n-1)} ${y(0)} L${x(0)} ${y(0)} Z`;
  const days=[0,Math.floor(n/2),n-1].map(i=>{const d=new Date(Date.UTC(2026,9,7)-(n-1-i)*864e5);return `<text x="${x(i)}" y="${H-P+16}" text-anchor="middle" font-size="10" fill="#66707d">${d.toISOString().slice(5,10)}</text>`;}).join('');
  $('t-chart').innerHTML=`<svg viewBox="0 0 ${W} ${H}" width="100%">${grid}${days}<path d="${area}" fill="rgba(37,99,235,.10)"/><path d="${path(U)}" stroke="#2563eb" stroke-width="2.2" fill="none"/>
    <path d="${path(L)}" stroke="#d93025" stroke-width="2" fill="none" stroke-dasharray="6 4"/>${alloc?`<path d="${path(alloc)}" stroke="#7c3aed" stroke-width="1.6" fill="none" stroke-dasharray="2 3"/>`:''}</svg>
    <div class="legend" style="flex-direction:row;gap:18px;margin-top:6px"><div class="row"><span class="sw" style="background:#2563eb"></span>usage</div><div class="row"><span class="sw" style="background:#d93025"></span>subscription limit (regional)${row.group?' · allocated from '+row.group:''}</div>${alloc?'<div class="row"><span class="sw" style="background:#7c3aed"></span>allocated from quota group</div>':''}</div>
    <div class="note" style="margin-top:8px">Phase 2: projected date to reach the limit at the current 14-day growth rate.</div>`;
  const ch=[]; for(let i=1;i<s.lim.length;i++) if(s.lim[i]!==s.lim[i-1]) ch.push({i,from:s.lim[i-1],to:s.lim[i]});
  $('t-log').innerHTML=ch.reverse().map(c=>{const d=new Date(Date.UTC(2026,9,7)-(89-c.i)*864e5).toISOString().slice(0,10);return `<div style="padding:8px 0;border-bottom:1px solid #f1f4f9"><b>${d}</b> · limit ${fmt(c.from)} → <b>${fmt(c.to)}</b> <span class="badge ok">+${fmt(c.to-c.from)}</span><div class="note">${row.group?'group allocation to subscription (GroupQuotaSubscriptionAllocation)':'subscription quota increase'}</div></div>`;}).join('')||'<div class="note">No limit changes in range.</div>';
}

// ---------------------------------------------------------------- FAMILIES
function renderFamilies(){
  $('f-table').innerHTML='<thead><tr><th>Series</th><th>Quota name</th><th>Category</th><th>CPU</th><th>Arch</th><th>Accelerator</th><th>Gen</th><th>Features</th><th>Lifecycle</th><th class="num">SKUs</th><th class="num">vCPU range</th><th class="num">Quotas held</th><th class="num">Total limit</th><th>Source</th></tr></thead><tbody>'+
    FAM.map(f=>{const rs=ROWS.filter(r=>r.fam===f.id); const v=f.skus.map(s=>s[1]);
      return `<tr><td class="fam">${f.series}</td><td><code>${f.id}</code></td><td>${CATLBL[f.cat]}</td><td><span class="chip ${CPUCLS[f.cpu]}">${CPULBL[f.cpu]}</span></td><td>${f.arch}</td><td>${f.acc?`<span class="chip c-gpu">${f.acc.vendor} ${f.acc.model}</span>`:'<span class="muted">—</span>'}</td><td>${f.gen}</td><td>${f.feats.map(x=>`<span class="chip c-feat">${FEATLBL[x]}</span>`).join('')}</td><td>${f.life==='Current'?'<span class="badge ok">current</span>':`<a class="badge warnb" href="${CAP_RESTRICT}" target="_blank">capacity-restricted</a>`}</td><td class="num">${f.skus.length}</td><td class="num">${Math.min(...v)}–${Math.max(...v)}</td><td class="num">${rs.length}</td><td class="num">${fmt(rs.reduce((a,r)=>a+r.lim,0))}</td><td><a href="${LEARN}" target="_blank">Learn ↗</a></td></tr>`;}).join('')+'</tbody>';
}

// ---------------------------------------------------------------- ADMIN
function renderAdmin(){
  $('a-kpis').innerHTML=[['b3','✅','Last full sync','13:02 UTC','07 Oct 2026 · 4 min 12 s'],['b1','🧾','Subscriptions covered','8 / 8','QuotaResources · all expected subs'],['b4','⚠️','Warnings','2','see coverage gaps'],['b2','📦','Rows ingested (24 h)',fmt(48216),'AQRSubQuota_CL + 7 tables']]
    .map(([b,ic,l,v,n])=>`<div class="kpi ${b}"><div class="ic">${ic}</div><div class="label">${l}</div><div class="val">${v}</div><div class="note">${n}</div></div>`).join('');
  const runs=[['13:00','SubQuota','ARG QuotaResources','8/8','2,184','0','Succeeded'],['13:01','Groups','Microsoft.Quota groupQuotas','2/2 groups','412','0','Succeeded'],['12:00','SubQuota','ARG QuotaResources','8/8','2,180','0','Succeeded'],
    ['09:00','Groups','Microsoft.Quota groupQuotas','2/2 groups','409','3','Succeeded'],['02:00','Zones/SKUs','Resource SKUs + locations','7/8','3,912','11','Warning'],['01:00','Catalog','vm-families.json ⨝ observed','28 families','28','0','Succeeded']];
  $('a-runs').innerHTML='<thead><tr><th>Start</th><th>Stage</th><th>Source</th><th>Coverage</th><th class="num">Rows</th><th class="num">429s</th><th>Status</th></tr></thead><tbody>'+runs.map(r=>`<tr><td>${r[0]}</td><td><b>${r[1]}</b></td><td>${r[2]}</td><td>${r[3]}</td><td class="num">${r[4]}</td><td class="num">${r[5]}</td><td>${r[6]==='Succeeded'?'<span class="badge ok">succeeded</span>':'<span class="badge warnb">warning</span>'}</td></tr>`).join('')+'</tbody>';
  $('a-gaps').innerHTML=`<div style="padding:6px 0;border-bottom:1px solid #f1f4f9"><span class="badge warnb">zones</span> <b>sub-sandbox-01</b>: Resource SKUs call returned 403 for <code>southeastasia</code>. Zone data for that sub/region is from the previous successful run (06 Oct).</div>
    <div style="padding:6px 0"><span class="badge warnb">rp</span> <b>sub-dev-shared-01</b>: <code>Microsoft.Quota</code> not registered. Required before it can join a Quota Group (<a href="https://learn.microsoft.com/azure/quotas/quota-groups" target="_blank">Learn</a>).</div>`;
  $('a-config').innerHTML=[['Management groups','mg-contoso (root)'],['Region scope','regions with usage or group quota (11)'],['Sync interval','SubQuota 1h · Groups 4h · Zones/SKUs daily'],['Data store','Log Analytics · law-aqr-prod (retention 90 d)'],['Network Security Perimeter','nsp-aqr · <span class="badge ok">Enforced</span> · inbound: AQR subscription (MI)'],['Identity','id-aqr (UAMI) · Reader @ mg-contoso'],['Auth','Easy Auth · Entra ID · app roles AQR.Reader / AQR.Admin · secretless (MI FIC)'],['API','/api/v1 · OpenAPI at /openapi/v1.json']].map(([k,v])=>`<div>${k}</div><div>${v}</div>`).join('');
}

// ---------------------------------------------------------------- wiring
function setZmode(m){ ZMODE=m; document.querySelectorAll('#zmode button,#zmode2 button').forEach(b=>b.classList.toggle('on',b.dataset.m===m)); renderExplorer(); renderZones(); }
document.querySelectorAll('#zmode button,#zmode2 button').forEach(b=>b.addEventListener('click',()=>setZmode(b.dataset.m)));
function show(p){ document.querySelectorAll('.page').forEach(x=>x.classList.toggle('on',x.id==='p-'+p)); document.querySelectorAll('#nav a').forEach(a=>a.classList.toggle('active',a.dataset.page===p)); }
document.querySelectorAll('#nav a').forEach(a=>a.addEventListener('click',()=>{ location.hash=a.dataset.page; }));
window.addEventListener('hashchange',()=>show(location.hash.slice(1)||'overview'));

$('g-sel').innerHTML=GROUPS.map(g=>`<option>${g.name}</option>`).join('');
const fillGReg=()=>{ const g=$('g-sel').value; const regs=[...new Set(GQ.filter(x=>x.group===g).map(x=>x.reg))]; $('g-region').innerHTML='<option value="*">All regions</option>'+regs.map(r=>`<option>${r}</option>`).join(''); };
fillGReg(); $('g-sel').addEventListener('change',()=>{fillGReg();renderGroups();}); $('g-region').addEventListener('change',renderGroups);
$('t-key').innerHTML=TKEYS.map((r,i)=>`<option value="${i}">${r.f.series} · ${r.reg} · ${r.sub}</option>`).join('');
$('t-key').addEventListener('change',renderTrends);
document.querySelectorAll('#t-range button').forEach(b=>b.addEventListener('click',()=>{TDAYS=+b.dataset.d;document.querySelectorAll('#t-range button').forEach(x=>x.classList.toggle('on',x===b));renderTrends();}));

buildFilters(); zoneSelectors();
renderOverview(); renderExplorer(); renderGroups(); renderZones(); renderTrends(); renderFamilies(); renderAdmin();
show(location.hash.slice(1)||'overview');
})();
