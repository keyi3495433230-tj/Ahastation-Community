// Preview-only geometry: never feed cropped coordinates into gaze estimation.
const clamp=(v,min,max)=>Math.max(min,Math.min(max,v));
export function cropAround(cx,cy,width,aspect,frameWidth,frameHeight){
  width=Math.min(width,frameWidth,frameHeight*aspect);
  const height=width/aspect;
  return {x:clamp(cx-width/2,0,frameWidth-width),y:clamp(cy-height/2,0,frameHeight-height),width,height};
}
export function previewCrops(points,width,height){
  if(!points || points.length<478 || !points.every(p=>Number.isFinite(p.x)&&Number.isFinite(p.y)))return null;
  const xs=points.map(p=>p.x*width),ys=points.map(p=>p.y*height);
  const left=Math.min(...xs),right=Math.max(...xs),top=Math.min(...ys),bottom=Math.max(...ys);
  const face=cropAround((left+right)/2,(top+bottom)/2,Math.max((right-left)*1.3,(bottom-top)*1.2*4/3,width*.25),4/3,width,height);
  const eyes=[33,133,362,263,159,145,386,374,468,473].map(i=>points[i]);
  const eyeX=eyes.map(p=>p.x*width),eyeY=eyes.map(p=>p.y*height);
  const eyeWidth=Math.max((Math.max(...eyeX)-Math.min(...eyeX))*1.55,width*.18);
  return {face,eyes:cropAround((Math.min(...eyeX)+Math.max(...eyeX))/2,(Math.min(...eyeY)+Math.max(...eyeY))/2,eyeWidth,4,width,height)};
}
export function smoothCrop(previous,next){
  if(!previous)return next;
  return Object.fromEntries(Object.keys(next).map(k=>[k,previous[k]+(next[k]-previous[k])*.25]));
}
export function previewPosition(width,height,panelWidth,panelHeight,x,y,obstacles=[]){
  const gap=12,radius=38;
  const candidates=[{left:gap,top:height-panelHeight-gap},{left:width-panelWidth-gap,top:height-panelHeight-gap},
    {left:gap,top:gap},{left:width-panelWidth-gap,top:gap}];
  return candidates.filter(p=>p.left>=gap && p.top>=gap && p.left+panelWidth<=width-gap && p.top+panelHeight<=height-gap)
    .filter(p=>x+radius<p.left || x-radius>p.left+panelWidth || y+radius<p.top || y-radius>p.top+panelHeight)
    .filter(p=>obstacles.every(r=>p.left+panelWidth+gap<r.left || p.left-gap>r.right || p.top+panelHeight+gap<r.top || p.top-gap>r.bottom))
    .sort((a,b)=>Math.hypot(x-(b.left+panelWidth/2),y-(b.top+panelHeight/2))-Math.hypot(x-(a.left+panelWidth/2),y-(a.top+panelHeight/2)))[0]??null;
}
