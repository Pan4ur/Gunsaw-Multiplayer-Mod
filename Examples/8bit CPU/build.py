import argparse, dataclasses, json, math, pathlib, re, sys, copy


VCC=400000; GND=400001; CPUCLK=400035
PCQ=[400036,400038,400040,400042,400044]; PCN=[x+1 for x in PCQ]
RDS=[404268+7*b for b in range(8)]
WREG={r:404406+84*r for r in range(8)}
DEBUG_REGS=[1,3]
W0C=[406143+7*b for b in range(8)]
P0_SET=406457; P0_CLR=406458; P1_SET=406459; P1_CLR=406460
P0_CLEAR_ALL=406461; P1_CLEAR_ALL=406468
BR1=404230; T1=[407892,407897,407902,407907,407912]
RA_BITS=[401611+15*b for b in range(8)]
RD_BITS=[401851+15*b for b in range(8)]
RAM_WRITE_ADDR_RD=401324
RAM_WRITE_DATA_RA=401354
RDSEL_SCRATCH=401147
ASEL_SCRATCH=401100
RAM_Q=[[400176+a*16+b*2 for b in range(8)] for a in range(32)]
VRAM0_Q=[400688+i*4 for i in range(64)]
VRAM1_Q=[400690+i*4 for i in range(64)]
RNG_Q=[400011+i*2 for i in range(8)]
DPAD_D0=400004; DPAD_D1=400008
BUTTON_IDS={'Up':101,'Down':102,'Left':103,'Right':104}

NATIVE_ALU_A=[940000+i for i in range(8)]
NATIVE_ALU_B=[940008+i for i in range(8)]
NATIVE_ALU_PASS=940016
NATIVE_ALU_ADD=940017
NATIVE_ALU_SUB=940018
NATIVE_ALU_AND=940019
NATIVE_ALU_OR=940020
NATIVE_ALU_XOR=940021
NATIVE_ALU_NOT=940022
NATIVE_ALU_OUT=[940032+i for i in range(8)]
NATIVE_ALU_ZERO=940040

@dataclasses.dataclass
class Token: kind:str; value:str; pos:int
TOKEN_RE=re.compile(
 r'''(?P<WS>\s+)|(?P<LINE>//[^\n]*)|(?P<BLOCK>/\*.*?\*/)|'''
 r'''(?P<HEX>0[xX][0-9A-Fa-f]+)|(?P<BIN>0[bB][01]+)|(?P<NUM>\d+)|'''
 r'''(?P<ID>[A-Za-z_][A-Za-z0-9_]*)|'''
 r'''(?P<OP>\+\+|--|\+=|-=|==|!=|<=|>=|&&|\|\||<<|>>|[+\-*/%&|^~!<>=.;,(){}\[\]])''',re.S)
KEYWORDS={'using','namespace','public','private','internal','protected','static','class','void','byte','bool','if','else','while','for','break','continue','return','true','false','unchecked'}
def lex(text):
 out=[];p=0
 while p<len(text):
  m=TOKEN_RE.match(text,p)
  if not m:
   line=text.count('\n',0,p)+1;col=p-text.rfind('\n',0,p);raise SyntaxError(f'unexpected character at {line}:{col}: {text[p:p+16]!r}')
  k=m.lastgroup;v=m.group()
  if k not in ('WS','LINE','BLOCK'):
   if k=='ID' and v in KEYWORDS:k=v
   elif k in ('HEX','BIN','NUM'):k='number'
   elif k=='OP':k=v
   out.append(Token(k,v,p))
  p=m.end()
 out.append(Token('EOF','',len(text)));return out

@dataclasses.dataclass
class Expr: pass
@dataclasses.dataclass
class Num(Expr): value:int
@dataclasses.dataclass
class BoolLit(Expr): value:bool
@dataclasses.dataclass
class Var(Expr): name:str
@dataclasses.dataclass
class Member(Expr): obj:str; member:str
@dataclasses.dataclass
class CallExpr(Expr): obj:str; method:str; args:list
@dataclasses.dataclass
class Unary(Expr): op:str; x:Expr
@dataclasses.dataclass
class Binary(Expr): op:str; a:Expr; b:Expr
@dataclasses.dataclass
class Cast(Expr): typ:str; x:Expr
@dataclasses.dataclass
class Stmt: pass
@dataclasses.dataclass
class Block(Stmt): items:list
@dataclasses.dataclass
class Decl(Stmt): typ:str; name:str; init:Expr|None
@dataclasses.dataclass
class Assign(Stmt): name:str; op:str; expr:Expr
@dataclasses.dataclass
class IncDec(Stmt): name:str; delta:int
@dataclasses.dataclass
class If(Stmt): cond:Expr; then_s:Stmt; else_s:Stmt|None
@dataclasses.dataclass
class While(Stmt): cond:Expr; body:Stmt
@dataclasses.dataclass
class Break(Stmt): pass
@dataclasses.dataclass
class Continue(Stmt): pass
@dataclasses.dataclass
class Return(Stmt): pass
@dataclasses.dataclass
class CallStmt(Stmt): obj:str; method:str; args:list

class Parser:
 def __init__(self,t):self.t=t;self.i=0
 def cur(self):return self.t[self.i]
 def at(self,k):return self.cur().kind==k
 def take(self,k=None):
  t=self.cur()
  if k is not None and t.kind!=k:raise SyntaxError(f'expected {k}, got {t.kind} near {t.value!r}')
  self.i+=1;return t
 def maybe(self,k):
  if self.at(k):self.i+=1;return True
  return False
 def parse(self):
  mains=[]
  while not self.at('EOF'):
   if self.at('using'):
    self.take()
    while not self.maybe(';'):self.take()
    continue
   if self.at('namespace'):
    self.take();self.qname();self.take('{');mains+=self.decls('}');continue
   mains+=self.one_decl()
  if len(mains)!=1:raise SyntaxError(f'expected exactly one static void Main(), found {len(mains)}')
  return mains[0]
 def qname(self):
  self.take('ID')
  while self.maybe('.'):self.take('ID')
 def decls(self,end):
  a=[]
  while not self.maybe(end):a+=self.one_decl()
  return a
 def mods(self):
  while self.cur().kind in ('public','private','internal','protected','static'):self.take()
 def one_decl(self):
  self.mods()
  if self.maybe('class'):
   self.take('ID');self.take('{');return self.class_body()
  raise SyntaxError(f'only class declarations are supported near {self.cur().value!r}')
 def class_body(self):
  mains=[]
  while not self.maybe('}'):
   self.mods();rt=self.cur().kind
   if rt not in ('void','byte','bool'):raise SyntaxError(f'unsupported class member near {self.cur().value!r}')
   self.take();name=self.take('ID').value;self.take('(')
   if not self.at(')'):raise SyntaxError('helper methods are not supported yet; inline into Main')
   self.take(')');body=self.block()
   if name=='Main' and rt=='void':mains.append(body)
   else:raise SyntaxError(f'helper method {name} is not supported yet; inline it into Main')
  return mains
 def block(self):
  self.take('{');a=[]
  while not self.maybe('}'):a.append(self.stmt())
  return Block(a)
 def stmt(self):
  if self.at('{'):return self.block()
  if self.at('byte') or self.at('bool'):
   typ=self.take().kind;name=self.take('ID').value;init=None
   if self.maybe('='):init=self.expr()
   self.take(';');return Decl(typ,name,init)
  if self.maybe('if'):
   self.take('(');c=self.expr();self.take(')');t=self.stmt();e=self.stmt() if self.maybe('else') else None;return If(c,t,e)
  if self.maybe('while'):
   self.take('(');c=self.expr();self.take(')');return While(c,self.stmt())
  if self.maybe('break'):self.take(';');return Break()
  if self.maybe('continue'):self.take(';');return Continue()
  if self.maybe('return'):
   if not self.at(';'):raise SyntaxError('Main only supports return;')
   self.take(';');return Return()
  if self.at('ID'):
   a=self.take('ID').value
   if self.maybe('.'):
    m=self.take('ID').value;self.take('(');args=[]
    if not self.at(')'):
     args.append(self.expr())
     while self.maybe(','):args.append(self.expr())
    self.take(')');self.take(';');return CallStmt(a,m,args)
   if self.maybe('++'):self.take(';');return IncDec(a,1)
   if self.maybe('--'):self.take(';');return IncDec(a,-1)
   if self.cur().kind in ('=','+=','-='):
    op=self.take().kind;e=self.expr();self.take(';');return Assign(a,op,e)
   raise SyntaxError(f'unsupported statement after {a}')
  raise SyntaxError(f'unsupported statement near {self.cur().value!r}')
 PRE={'||':1,'&&':2,'|':3,'^':4,'&':5,'==':6,'!=':6,'<':7,'<=':7,'>':7,'>=':7,'<<':8,'>>':8,'+':9,'-':9}
 def expr(self,minp=1):
  x=self.unary()
  while self.cur().kind in self.PRE and self.PRE[self.cur().kind]>=minp:
   op=self.take().kind;p=self.PRE[op];y=self.expr(p+1);x=Binary(op,x,y)
  return x
 def unary(self):
  if self.cur().kind in ('!','~','-'):
   op=self.take().kind;return Unary(op,self.unary())
  if self.at('(') and self.t[self.i+1].kind in ('byte','bool') and self.t[self.i+2].kind==')':
   self.take('(');typ=self.take().kind;self.take(')');return Cast(typ,self.unary())
  if self.maybe('unchecked'):
   self.take('(');x=self.expr();self.take(')');return x
  return self.primary()
 def primary(self):
  if self.at('number'):return Num(int(self.take().value,0))
  if self.maybe('true'):return BoolLit(True)
  if self.maybe('false'):return BoolLit(False)
  if self.at('ID'):
   a=self.take().value
   if self.maybe('.'):
    b=self.take('ID').value
    if self.maybe('('):
     args=[]
     if not self.at(')'):
      args.append(self.expr())
      while self.maybe(','):args.append(self.expr())
     self.take(')');return CallExpr(a,b,args)
    return Member(a,b)
   return Var(a)
  if self.maybe('('):
   x=self.expr();self.take(')');return x
  raise SyntaxError(f'expected expression near {self.cur().value!r}')

@dataclasses.dataclass
class Label:name:str
@dataclasses.dataclass
class Op:kind:str;data:object=None
class IRBuilder:
 def __init__(self):self.items=[];self.n=0;self.loop=[]
 def label(self,p='L'):x=f'{p}{self.n}';self.n+=1;return x
 def mark(self,l):self.items.append(Label(l))
 def emit(self,k,d=None):self.items.append(Op(k,d))
 def compile_stmt(self,s):
  if isinstance(s,Block):
   for x in s.items:self.compile_stmt(x)
  elif isinstance(s,Decl):self.emit('decl',s)
  elif isinstance(s,Assign):self.emit('assign',s)
  elif isinstance(s,IncDec):self.emit('incdec',s)
  elif isinstance(s,CallStmt):
   if s.obj=='Cpu' and s.method=='Wait':
    if len(s.args)!=1 or not isinstance(s.args[0],Num):raise SyntaxError('Cpu.Wait requires one integer literal')
    for _ in range(max(0,s.args[0].value)):self.emit('nop')
   elif s.obj=='Ram' and s.method=='Write':
    if len(s.args)!=2:raise SyntaxError('Ram.Write(address, value)')
    self.emit('ram_addr',s.args[0]);self.emit('ram_data',s.args[1]);self.emit('ram_commit')
   else:self.emit('call',s)
  elif isinstance(s,If):
   le=self.label('else');ln=self.label('endif');self.emit('brfalse',(s.cond,le if s.else_s else ln));self.compile_stmt(s.then_s)
   if s.else_s:self.emit('goto',ln);self.mark(le);self.compile_stmt(s.else_s)
   self.mark(ln)
  elif isinstance(s,While):
   lc=self.label('while');le=self.label('wend');self.mark(lc);self.emit('brfalse',(s.cond,le));self.loop.append((lc,le));self.compile_stmt(s.body);self.loop.pop();self.emit('goto',lc);self.mark(le)
  elif isinstance(s,Break):
   if not self.loop:raise SyntaxError('break outside while')
   self.emit('goto',self.loop[-1][1])
  elif isinstance(s,Continue):
   if not self.loop:raise SyntaxError('continue outside while')
   self.emit('goto',self.loop[-1][0])
  elif isinstance(s,Return):self.emit('return')
  else:raise TypeError(s)
 def finalize(self,body):
  self.compile_stmt(body);self.emit('halt');labels={};ops=[]
  for x in self.items:
   if isinstance(x,Label):labels[x.name]=len(ops)
   else:ops.append(x)
  if len(ops)>1024:raise ValueError(f'program needs {len(ops)} microstates; max 1024')
  for op in ops:
   if op.kind=='goto':op.data=labels[op.data]
   elif op.kind=='brfalse':op.data=(op.data[0],labels[op.data[1]])
  return ops


import base64, hashlib, zlib

VCC=400000; GND=400001; CPUCLK=400035
PCQ=[400036,400038,400040,400042,400044,920390,920392]
PCN=[400037,400039,400041,400043,400045,920391,920393]
REGQ=[[400046+r*16+b*2 for b in range(8)] for r in range(8)]
RNG_Q=[400011+i*2 for i in range(8)]
DPAD_D0=400004; DPAD_D1=400008
BUTTON_IDS={'Up':101,'Down':102,'Left':103,'Right':104}
RAM_Q=[[400176+a*16+b*2 for b in range(8)] for a in range(32)]
VRAM0_Q=[400688+i*4 for i in range(64)]; VRAM1_Q=[400690+i*4 for i in range(64)]
RAM_READ_BITS=[402363+126*b for b in range(8)]
GPU_READ=403861
W0C=[406143+7*b for b in range(8)]; W1C=[406199+7*b for b in range(8)]
W0_P0_SET=406457;W0_P0_CLR=406458;W0_P1_SET=406459;W0_P1_CLR=406460
W1_P0_SET=406463;W1_P0_CLR=406464;W1_P1_SET=406465;W1_P1_CLR=406466
P0_CLEAR_ALL=406461;P1_CLEAR_ALL=406468
BR1=404230;BR2=404257

REG_WE=[920000+r for r in range(8)]
REG_DATA=[[920040+r*8+b for b in range(8)] for r in range(8)]
PC_D=[920120+i for i in range(7)]
RAM_WE=920130; RAM_WADDR=[920131+i for i in range(5)]; RAM_WDATA=[920136+i for i in range(8)]
RAM_RADDR=[920150+i for i in range(5)]
VR_RADDR=[920160+i for i in range(8)]; VR_P0=920168; VR_P1=920169

CUSTOM='MP/CustomProp'; ROM_PREFIX='ROM/'

def custom_part(x,y,uid,typ,data):
    meta={'version':1,'uid':uid,'type':'MP/Logic/'+typ,'data':json.dumps(data,separators=(',',':'))}
    return {'pos':{'x':round(x*4)/4,'y':round(y*4)/4},'rot':0.0,'path':CUSTOM,'id':0,'activId':0,
            'team':json.dumps(meta,separators=(',',':')),'size':{'x':0.0,'y':0.0},'force':{'x':0.0,'y':0.0}}

def parse_custom(p):
    if p.get('path')!=CUSTOM:return None
    try:
        m=json.loads(p['team']);return m,json.loads(m['data'])
    except:return None

def decode_level(path):
    s=pathlib.Path(path).read_text().strip()
    return json.loads(s) if s.startswith('{') else json.loads(zlib.decompress(base64.b64decode(s),-15).decode())

def encode_level(lv):
    raw=json.dumps(lv,ensure_ascii=False,separators=(',',':')).encode();z=zlib.compressobj(1,zlib.DEFLATED,-15)
    return base64.b64encode(z.compress(raw)+z.flush()).decode()

def hw_hash(lv):
    ps=[]
    for p in lv['parts']:
        z=parse_custom(p)
        if z and z[0].get('uid','').startswith(ROM_PREFIX):continue
        ps.append(p)
    return hashlib.sha256(json.dumps(ps,ensure_ascii=False,separators=(',',':')).encode()).hexdigest()

class Net:
    def __init__(self,name):
        self.name=re.sub(r'[^A-Za-z0-9_.-]+','_',name);self.parts=[];self.next=950000;self.i=0;self.targets={};self.not_cache={}
    def fresh(self):s=self.next;self.next+=1;return s
    def pos(self):
        i=self.i;self.i+=1;return (70+(i%72)*1.0,115-(i//72)*0.75)
    def gate(self,typ,data,label,out=None):
        if out is None:out=self.fresh()
        d=dict(data);d['output']=out
        x,y=self.pos();self.parts.append(custom_part(x,y,f'ROM/NATIVE/{self.name}/{self.i:05d}-{label}',typ,d));return out
    def line(self,typ,data,label):
        x,y=self.pos();self.parts.append(custom_part(x,y,f'ROM/NATIVE/{self.name}/{self.i:05d}-{label}',typ,data))
    def AND(self,a,b,n):
        if a==GND or b==GND:return GND
        if a==VCC:return b
        if b==VCC:return a
        if a==b:return a
        return self.gate('AND',{'inputA':a,'inputB':b},n)
    def OR(self,a,b,n):
        if a==VCC or b==VCC:return VCC
        if a==GND:return b
        if b==GND:return a
        if a==b:return a
        return self.gate('OR',{'inputA':a,'inputB':b},n)
    def XOR(self,a,b,n):
        if a==GND:return b
        if b==GND:return a
        if a==b:return GND
        if a==VCC:return self.NOT(b,n+'/nv')
        if b==VCC:return self.NOT(a,n+'/nv')
        return self.gate('XOR',{'inputA':a,'inputB':b},n)
    def NOT(self,a,n,cache=True):
        if a==GND:return VCC
        if a==VCC:return GND
        if cache and a in self.not_cache:return self.not_cache[a]
        o=self.gate('NOT',{'input':a},n)
        if cache:self.not_cache[a]=o
        return o
    def EDGE(self,a,mode,n,out=None):return self.gate('EDGE',{'input':a,'mode':mode},n,out)
    def SR(self,s,r,q,nq,initial,n):self.line('SR',{'set':s,'reset':r,'q':q,'notQ':nq,'initialQ':int(initial)},n)
    def DFF(self,d,c,q,nq,initial,n):self.line('DFF',{'d':d,'clock':c,'q':q,'notQ':nq,'initialQ':int(initial)},n)
    def ands(self,xs,n):
        xs=[x for x in xs if x!=VCC]
        if any(x==GND for x in xs):return GND
        if not xs:return VCC
        k=0
        while len(xs)>1:
            ys=[]
            for i in range(0,len(xs),2):ys.append(xs[i] if i+1==len(xs) else self.AND(xs[i],xs[i+1],f'{n}/a{k}-{i//2}'))
            xs=ys;k+=1
        return xs[0]
    def ors(self,xs,n):
        xs=[x for x in xs if x!=GND]
        if any(x==VCC for x in xs):return VCC
        if not xs:return GND
        k=0
        while len(xs)>1:
            ys=[]
            for i in range(0,len(xs),2):ys.append(xs[i] if i+1==len(xs) else self.OR(xs[i],xs[i+1],f'{n}/o{k}-{i//2}'))
            xs=ys;k+=1
        return xs[0]
    def term(self,target,s):
        if s!=GND:self.targets.setdefault(target,[]).append(s)
    def route_bits(self,targets,en,bits,n):
        for i,(t,b) in enumerate(zip(targets,bits)):
            if b==GND:continue
            self.term(t,en if b==VCC else self.AND(en,b,f'{n}/b{i}'))
    def fixed(self,targets,en,val,n):
        for i,t in enumerate(targets):
            if (val>>i)&1:self.term(t,en)
    def finish(self):
        for t,ts in sorted(self.targets.items()):
            s=self.ors(ts,f'OUT/{t}')
            if s!=t:self.gate('OR',{'inputA':s,'inputB':GND},f'OUT/{t}/buf',out=t)

@dataclasses.dataclass
class Value:
    typ:str;bits:list;ready:int|None=None
    @property
    def bit(self):return self.bits[0]

@dataclasses.dataclass
class VarInfo:
    typ:str;reg:int

@dataclasses.dataclass
class PureIfOp:
    stmt:If

def is_pure_stmt(s):
    if isinstance(s,(Assign,IncDec)):return True
    if isinstance(s,Block):return all(is_pure_stmt(x) for x in s.items)
    if isinstance(s,If):return is_pure_stmt(s.then_s) and (s.else_s is None or is_pure_stmt(s.else_s))
    return False

class NativeIRBuilder(IRBuilder):
    def compile_stmt(self,s):
        if isinstance(s,Block):
            for x in s.items:self.compile_stmt(x)
        elif isinstance(s,Decl):self.emit('decl',s)
        elif isinstance(s,Assign):self.emit('assign',s)
        elif isinstance(s,IncDec):self.emit('incdec',s)
        elif isinstance(s,CallStmt):
            if s.obj=='Cpu' and s.method=='Wait':
                if len(s.args)!=1 or not isinstance(s.args[0],Num):raise SyntaxError('Cpu.Wait requires literal')
                for _ in range(max(0,s.args[0].value)):self.emit('nop')
            else:self.emit('call',s)
        elif isinstance(s,If) and is_pure_stmt(s):self.emit('pure_if',s)
        elif isinstance(s,If):
            le=self.label('else');ln=self.label('endif');self.emit('brfalse',(s.cond,le if s.else_s else ln));self.compile_stmt(s.then_s)
            if s.else_s:self.emit('goto',ln);self.mark(le);self.compile_stmt(s.else_s)
            self.mark(ln)
        elif isinstance(s,While):
            lc=self.label('while');le=self.label('wend');self.mark(lc)
            if not (isinstance(s.cond,BoolLit) and s.cond.value):
                self.emit('brfalse',(s.cond,le))
            self.loop.append((lc,le));self.compile_stmt(s.body);self.loop.pop();self.emit('goto',lc);self.mark(le)
        elif isinstance(s,Break):
            if not self.loop:raise SyntaxError('break outside while')
            self.emit('goto',self.loop[-1][1])
        elif isinstance(s,Continue):
            if not self.loop:raise SyntaxError('continue outside while')
            self.emit('goto',self.loop[-1][0])
        elif isinstance(s,Return):self.emit('return')
        else:raise TypeError(s)

def call_resources(c):
    if c.obj=='Ram' and c.method=='Write':return (1,0,False)
    if c.obj in ('Vram0','Vram1') and c.method in ('Set','Clear'):return (0,1,False)
    if c.obj in ('Vram0','Vram1') and c.method=='ClearAll':return (0,0,True)
    if c.obj=='Cpu' and c.method=='Halt':return (0,0,False)
    return (0,0,False)

def expr_has_call(e,obj=None,method=None):
    if e is None:return False
    if isinstance(e,CallExpr):
        if (obj is None or e.obj==obj) and (method is None or e.method==method):return True
        return any(expr_has_call(x,obj,method) for x in e.args)
    if isinstance(e,Binary):return expr_has_call(e.a,obj,method) or expr_has_call(e.b,obj,method)
    if isinstance(e,Unary):return expr_has_call(e.x,obj,method)
    if isinstance(e,Cast):return expr_has_call(e.x,obj,method)
    return False

def op_has_ram_read(o):
    if o.kind=='assign':return expr_has_call(o.data.expr,'Ram','Read')
    if o.kind=='decl':return expr_has_call(o.data.init,'Ram','Read')
    if o.kind=='brfalse':return expr_has_call(o.data[0],'Ram','Read')
    return False

def op_has_vram_read(o):
    if o.kind=='assign':return expr_has_call(o.data.expr,'Vram0','Read') or expr_has_call(o.data.expr,'Vram1','Read')
    if o.kind=='decl':return expr_has_call(o.data.init,'Vram0','Read') or expr_has_call(o.data.init,'Vram1','Read')
    if o.kind=='brfalse':return expr_has_call(o.data[0],'Vram0','Read') or expr_has_call(o.data[0],'Vram1','Read')
    return False

def bundle_ops(ops):
    targets=set()
    for i,o in enumerate(ops):
        if o.kind=='goto':targets.add(o.data)
        elif o.kind=='brfalse':targets.add(o.data[1])
    groups=[];old_to_group={};cur=[];ram=vr=0;glob=False
    def flush():
        nonlocal cur,ram,vr,glob
        if cur:
            gi=len(groups);groups.append(cur)
            for oi,_ in cur:old_to_group[oi]=gi
        cur=[];ram=vr=0;glob=False
    for i,o in enumerate(ops):
        if i in targets and cur:flush()
        if op_has_ram_read(o) or op_has_vram_read(o):
            flush();cur=[(i,o)];flush();continue
        if o.kind=='nop':
            flush()
            cur=[(i,o)]
            flush()
            continue
        if o.kind=='brfalse' and cur:
            if any(x.kind=='call' for _,x in cur):flush()
        add_ram=add_vr=0;add_glob=False
        if o.kind=='call':add_ram,add_vr,add_glob=call_resources(o.data)
        if cur and o.kind=='call' and o.data.obj in ('Vram0','Vram1') and o.data.method=='Clear':
            plane=o.data.obj
            if any(x.kind=='call' and x.data.obj==plane and x.data.method=='Set' for _,x in cur):
                flush()
        if cur and (ram+add_ram>1 or vr+add_vr>2 or (glob and add_vr) or (add_glob and vr)):
            flush()
        cur.append((i,o));ram+=add_ram;vr+=add_vr;glob=glob or add_glob
        if o.kind in ('brfalse','goto','return','halt') or (o.kind=='call' and o.data.obj=='Cpu' and o.data.method=='Halt'):
            flush()
    flush()
    for g in groups:
        pass
    return groups,old_to_group

class NativeCompiler:
    def __init__(self,name,ops):
        self.name=name;self.oldops=ops;self.groups,self.old2g=bundle_ops(ops);self.net=Net(name);self.vars={};self.env=None;self.state_index=0
        if len(self.groups)>128:raise ValueError(f'{name}: {len(self.groups)} wide states; maximum 128')
        self.state=[]
        for v in range(len(self.groups)):
            lits=[PCQ[b] if (v>>b)&1 else PCN[b] for b in range(7)]
            self.state.append(self.net.ands(lits,f'PC={v}'))
        self.ram_read_key=None;self.vram_read_key=None;self.vr_slot=0;self.ram_used=False;self.path_instances=[];self.alu_used=False
        self.collect_vars()
    def collect_vars(self):
        decl=[]
        for o in self.oldops:
            if o.kind=='decl':
                d=o.data
                if d.name not in [x.name for x in decl]:decl.append(d)
        if len(decl)>8:raise ValueError(f'{self.name}: {len(decl)} variables; native register file has 8. Simplify source.')
        for r,d in enumerate(decl):self.vars[d.name]=VarInfo(d.typ,r)
    def cbyte(self,v):
        v&=255;return Value('byte',[VCC if (v>>b)&1 else GND for b in range(8)])
    def cbool(self,v):return Value('bool',[VCC if v else GND]+[GND]*7)
    def varval(self,n):
        if n not in self.vars:raise NameError(n)
        q=REGQ[self.vars[n].reg]
        return Value(self.vars[n].typ,q if self.vars[n].typ=='byte' else [q[0]]+[GND]*7)
    def getvar(self,n):return self.env.get(n,self.varval(n))
    def boolsig(self,v):
        if v.typ=='bool':return v.bits[0]
        return self.net.ors(v.bits,f'S{self.state_index}/nz')
    def hw_alu(self,a,b,op,n):
        if self.alu_used:return None
        self.alu_used=True
        en=self.state[self.state_index]
        self.net.route_bits(NATIVE_ALU_A,en,a,f'S{self.state_index}/ALUA')
        self.net.route_bits(NATIVE_ALU_B,en,b,f'S{self.state_index}/ALUB')
        opline={'PASS':NATIVE_ALU_PASS,'ADD':NATIVE_ALU_ADD,'SUB':NATIVE_ALU_SUB,
                'AND':NATIVE_ALU_AND,'OR':NATIVE_ALU_OR,'XOR':NATIVE_ALU_XOR,
                'NOT':NATIVE_ALU_NOT}[op]
        self.net.term(opline,en)
        return NATIVE_ALU_OUT[:]
    def alu_or_fallback(self,a,b,op,n):
        h=self.hw_alu(a,b,op,n)
        if h is not None:return h
        if op=='ADD':return self.add8(a,b,n)
        if op=='SUB':return self.add8(a,[self.net.NOT(x,n+f'/nb{i}') for i,x in enumerate(b)],n+'/sub',VCC)
        if op=='AND':return [self.net.AND(x,y,n+f'/and{i}') for i,(x,y) in enumerate(zip(a,b))]
        if op=='OR':return [self.net.OR(x,y,n+f'/or{i}') for i,(x,y) in enumerate(zip(a,b))]
        if op=='XOR':return [self.net.XOR(x,y,n+f'/xor{i}') for i,(x,y) in enumerate(zip(a,b))]
        if op=='NOT':return [self.net.NOT(x,n+f'/inv{i}') for i,x in enumerate(a)]
        raise ValueError(op)
    def add8(self,a,b,n,cin=GND):
        out=[];c=cin
        for i in range(8):
            x=self.net.XOR(a[i],b[i],f'{n}/x{i}');out.append(self.net.XOR(x,c,f'{n}/s{i}'))
            c=self.net.OR(self.net.AND(a[i],b[i],f'{n}/ab{i}'),self.net.AND(x,c,f'{n}/xc{i}'),f'{n}/c{i}')
        return out
    def eq8(self,a,b,n):return self.net.NOT(self.net.ors([self.net.XOR(x,y,f'{n}/x{i}') for i,(x,y) in enumerate(zip(a,b))],n+'/ne'),n+'/eq')
    def lt8(self,a,b,n):
        eq=VCC;ts=[]
        for i in range(7,-1,-1):
            lt=self.net.AND(self.net.NOT(a[i],f'{n}/na{i}'),b[i],f'{n}/lt{i}');ts.append(self.net.AND(eq,lt,f'{n}/t{i}'))
            eq=self.net.AND(eq,self.net.NOT(self.net.XOR(a[i],b[i],f'{n}/x{i}'),f'{n}/xe{i}'),f'{n}/eq{i}')
        return self.net.ors(ts,n+'/sum')
    def mux(self,c,a,b,n):
        nc=self.net.NOT(c,n+'/nc')
        return [self.net.OR(self.net.AND(c,x,f'{n}/t{i}'),self.net.AND(nc,y,f'{n}/f{i}'),f'{n}/o{i}') for i,(x,y) in enumerate(zip(a,b))]
    def ram_read(self,a):
        key=tuple(a.bits[:5])
        if self.ram_read_key is not None and self.ram_read_key!=key:
            raise ValueError(f'{self.name}: two different RAM read addresses share state {self.state_index}')
        if self.ram_read_key is None:
            self.ram_read_key=key;en=self.state[self.state_index]
            self.net.route_bits(RAM_RADDR,en,a.bits[:5],f'S{self.state_index}/RAMRADDR')
        return Value('byte',RAM_READ_BITS)
    def vram_read(self,a,plane):
        key=(plane,tuple(a.bits))
        if self.vram_read_key is not None and self.vram_read_key!=key:
            raise ValueError(f'{self.name}: two different VRAM read addresses/planes share state {self.state_index}')
        if self.vram_read_key is None:
            self.vram_read_key=key;en=self.state[self.state_index]
            self.net.route_bits(VR_RADDR,en,a.bits,f'S{self.state_index}/VRADDR')
            self.net.term(VR_P0 if plane==0 else VR_P1,en)
        return Value('bool',[GPU_READ]+[GND]*7)
    def expr(self,e):
        if isinstance(e,Num):return self.cbyte(e.value)
        if isinstance(e,BoolLit):return self.cbool(e.value)
        if isinstance(e,Var):return self.getvar(e.name)
        if isinstance(e,Member):
            if e.obj=='Random' and e.member=='Byte':return Value('byte',RNG_Q)
            if e.obj=='InputKeys' and e.member=='Direction':return Value('byte',[DPAD_D0,DPAD_D1]+[GND]*6)
            if e.obj=='InputKeys' and e.member in BUTTON_IDS:return Value('bool',[BUTTON_IDS[e.member]]+[GND]*7)
            if e.obj=='InputKeys' and e.member.endswith('Pressed') and e.member[:-7] in BUTTON_IDS:
                return Value('bool',[BUTTON_IDS[e.member[:-7]]]+[GND]*7)
            raise NameError(f'unsupported member {e.obj}.{e.member}')
        if isinstance(e,CallExpr):
            obj,m=e.obj,e.method
            if obj=='Coord' and m=='Make':
                x=self.expr(e.args[0]);y=self.expr(e.args[1]);return Value('byte',[x.bits[0],x.bits[1],x.bits[2],GND,y.bits[0],y.bits[1],y.bits[2],GND])
            if obj=='Coord' and m=='X':
                a=self.expr(e.args[0]);return Value('byte',[a.bits[0],a.bits[1],a.bits[2]]+[GND]*5)
            if obj=='Coord' and m=='Y':
                a=self.expr(e.args[0]);return Value('byte',[a.bits[4],a.bits[5],a.bits[6]]+[GND]*5)
            if obj=='Ram' and m=='Read':return self.ram_read(self.expr(e.args[0]))
            if obj=='Vram0' and m=='Read':return self.vram_read(self.expr(e.args[0]),0)
            if obj=='Vram1' and m=='Read':return self.vram_read(self.expr(e.args[0]),1)
            if obj=='Pathfinder' and m=='Find':return self.pathfinder(self.expr(e.args[0]),self.expr(e.args[1]))
            raise NameError(f'unsupported intrinsic {obj}.{m}')
        if isinstance(e,Cast):
            v=self.expr(e.x);return v if e.typ=='byte' else Value('bool',[self.boolsig(v)]+[GND]*7)
        if isinstance(e,Unary):
            v=self.expr(e.x)
            if e.op=='!':return Value('bool',[self.net.NOT(self.boolsig(v),f'S{self.state_index}/not')]+[GND]*7)
            if e.op=='~':return Value('byte',self.alu_or_fallback(v.bits,[GND]*8,'NOT',f'S{self.state_index}/inv'))
            if e.op=='-':return Value('byte',self.alu_or_fallback([GND]*8,v.bits,'SUB',f'S{self.state_index}/neg'))
        if isinstance(e,Binary):
            a=self.expr(e.a);b=self.expr(e.b);op=e.op;n=f'S{self.state_index}/EX{self.net.i}'
            if op=='+':return Value('byte',self.alu_or_fallback(a.bits,b.bits,'ADD',n))
            if op=='-':return Value('byte',self.alu_or_fallback(a.bits,b.bits,'SUB',n))
            if op=='&':return Value('byte',self.alu_or_fallback(a.bits,b.bits,'AND',n))
            if op=='|':return Value('byte',self.alu_or_fallback(a.bits,b.bits,'OR',n))
            if op=='^':return Value('byte',self.alu_or_fallback(a.bits,b.bits,'XOR',n))
            if op in ('==','!='):
                s=self.eq8(a.bits,b.bits,n);s=s if op=='==' else self.net.NOT(s,n+'/ne');return Value('bool',[s]+[GND]*7)
            if op in ('<','<=','>','>='):
                if op=='<':s=self.lt8(a.bits,b.bits,n)
                elif op=='>':s=self.lt8(b.bits,a.bits,n)
                elif op=='<=':s=self.net.NOT(self.lt8(b.bits,a.bits,n),n+'/le')
                else:s=self.net.NOT(self.lt8(a.bits,b.bits,n),n+'/ge')
                return Value('bool',[s]+[GND]*7)
            if op=='&&':return Value('bool',[self.net.AND(self.boolsig(a),self.boolsig(b),n+'/land')]+[GND]*7)
            if op=='||':return Value('bool',[self.net.OR(self.boolsig(a),self.boolsig(b),n+'/lor')]+[GND]*7)
            if op=='<<':
                if not isinstance(e.b,Num):raise ValueError('shift count must literal')
                k=e.b.value;return Value('byte',([GND]*k+a.bits[:8-k])[:8])
            if op=='>>':
                if not isinstance(e.b,Num):raise ValueError('shift count must literal')
                k=e.b.value;return Value('byte',(a.bits[k:]+[GND]*k)[:8])
            raise ValueError(f'unsupported op {op}')
        raise TypeError(e)
    def symbolic_pure(self,s,env):
        old=self.env;self.env=env
        if isinstance(s,Block):
            for x in s.items:env=self.symbolic_pure(x,env);self.env=env
        elif isinstance(s,Assign):
            cur=self.getvar(s.name);v=self.expr(s.expr)
            if s.op=='+=':v=Value('byte',self.alu_or_fallback(cur.bits,v.bits,'ADD',f'S{self.state_index}/pa'))
            elif s.op=='-=':v=Value('byte',self.alu_or_fallback(cur.bits,v.bits,'SUB',f'S{self.state_index}/ps'))
            env=dict(env);env[s.name]=v
        elif isinstance(s,IncDec):
            cur=self.getvar(s.name);one=self.cbyte(1)
            v=Value('byte',self.alu_or_fallback(cur.bits,one.bits,'ADD',f'S{self.state_index}/inc')) if s.delta>0 else Value('byte',self.alu_or_fallback(cur.bits,one.bits,'SUB',f'S{self.state_index}/dec'))
            env=dict(env);env[s.name]=v
        elif isinstance(s,If):
            c=self.boolsig(self.expr(s.cond));base=dict(env)
            te=self.symbolic_pure(s.then_s,dict(base));self.env=base
            ee=self.symbolic_pure(s.else_s,dict(base)) if s.else_s else dict(base)
            out=dict(base)
            for n in self.vars:
                tv=te.get(n,base.get(n,self.varval(n)));ev=ee.get(n,base.get(n,self.varval(n)))
                if tv.bits!=ev.bits:
                    out[n]=Value(self.vars[n].typ,self.mux(c,tv.bits,ev.bits,f'S{self.state_index}/IF/{n}'))
            env=out
        else:raise TypeError('non-pure in pure_if')
        self.env=old;return env
    def write_vram(self,plane,set_,addr):
        en=self.state[self.state_index]
        slot=self.vr_slot;self.vr_slot+=1
        if slot>=2:raise RuntimeError('bundler allowed >2 VRAM writes')
        coords=W0C if slot==0 else W1C
        self.net.route_bits(coords,en,addr.bits,f'S{self.state_index}/VW{slot}C')
        table0={(0,True):W0_P0_SET,(0,False):W0_P0_CLR,(1,True):W0_P1_SET,(1,False):W0_P1_CLR}
        table1={(0,True):W1_P0_SET,(0,False):W1_P0_CLR,(1,True):W1_P1_SET,(1,False):W1_P1_CLR}
        self.net.term((table0 if slot==0 else table1)[(plane,set_)],en)
    def ram_write(self,addr,data):
        en=self.state[self.state_index]
        self.net.term(RAM_WE,en);self.net.route_bits(RAM_WADDR,en,addr.bits[:5],f'S{self.state_index}/RWA');self.net.route_bits(RAM_WDATA,en,data.bits,f'S{self.state_index}/RWD')
    def call_stmt(self,c):
        obj,m=c.obj,c.method
        if obj=='Ram' and m=='Write':self.ram_write(self.expr(c.args[0]),self.expr(c.args[1]));return
        if obj in ('Vram0','Vram1') and m in ('Set','Clear'):
            self.write_vram(0 if obj=='Vram0' else 1,m=='Set',self.expr(c.args[0]));return
        if obj in ('Vram0','Vram1') and m=='ClearAll':self.net.term(P0_CLEAR_ALL if obj=='Vram0' else P1_CLEAR_ALL,self.state[self.state_index]);return
        if obj=='Cpu' and m=='Halt':return
        raise NameError(f'unsupported statement intrinsic {obj}.{m}')
    def pathfinder(self,head,food):
        key=self.state_index
        if hasattr(self,'_path_value') and key in self._path_value:return self._path_value[key]
        if not hasattr(self,'_path_value'):self._path_value={}
        active=self.state[self.state_index];nactive=self.net.NOT(active,f'PATH{key}/nactive')
        reach=[];nr=[]
        for i in range(64):reach.append(self.net.fresh());nr.append(self.net.fresh())
        def eqcoord(bits,coord,n):return self.eq8(bits,self.cbyte(coord).bits,n)
        for i in range(64):
            x=i&7;y=i>>3;coord=(y<<4)|x
            seed=eqcoord(food.bits,coord,f'PATH{key}/seed{i}')
            neigh=[]
            if x>0:neigh.append(reach[i-1])
            if x<7:neigh.append(reach[i+1])
            if y>0:neigh.append(reach[i-8])
            if y<7:neigh.append(reach[i+8])
            wave=self.net.ors(neigh,f'PATH{key}/nb{i}')
            free=self.net.NOT(VRAM0_Q[i],f'PATH{key}/free{i}')
            prop=self.net.AND(wave,free,f'PATH{key}/prop{i}')
            s=self.net.AND(active,self.net.OR(seed,prop,f'PATH{key}/setraw{i}'),f'PATH{key}/set{i}')
            self.net.SR(s,nactive,reach[i],nr[i],0,f'PATH{key}/R{i}')
        dirs=[]
        for di,(dx,dy) in enumerate([(1,0),(0,1),(-1,0),(0,-1)]):
            ts=[]
            for i in range(64):
                x=i&7;y=i>>3;nx=x+dx;ny=y+dy
                if 0<=nx<8 and 0<=ny<8:
                    src=(y<<4)|x;ni=ny*8+nx
                    ts.append(self.net.AND(eqcoord(head.bits,src,f'PATH{key}/h{di}_{i}'),reach[ni],f'PATH{key}/d{di}_{i}'))
            dirs.append(self.net.ors(ts,f'PATH{key}/dir{di}'))
        anyp=self.net.ors(dirs,f'PATH{key}/any')
        edge=self.net.EDGE(anyp,0,f'PATH{key}/foundedge')
        dq=[self.net.fresh() for _ in range(4)];dn=[self.net.fresh() for _ in range(4)]
        for i in range(4):self.net.DFF(dirs[i],edge,dq[i],dn[i],0,f'PATH{key}/DIR{i}')

        valid=self.net.fresh();nvalid=self.net.fresh();self.net.SR(edge,nactive,valid,nvalid,0,f'PATH{key}/VALID')
        cpu_rise=self.net.EDGE(CPUCLK,0,f'PATH{key}/CPU_RISE')
        ready_set=self.net.AND(valid,cpu_rise,f'PATH{key}/READY_SET')
        ready=self.net.fresh();nready=self.net.fresh();self.net.SR(ready_set,nactive,ready,nready,0,f'PATH{key}/READY')

        r0,d0,l0,u0=dq
        nr=self.net.NOT(r0,f'PATH{key}/nR')
        d=self.net.AND(nr,d0,f'PATH{key}/Dpri')
        rd=self.net.OR(r0,d,f'PATH{key}/RD')
        nrd=self.net.NOT(rd,f'PATH{key}/nRD')
        l=self.net.AND(nrd,l0,f'PATH{key}/Lpri')
        rdl=self.net.OR(rd,l,f'PATH{key}/RDL')
        nrdl=self.net.NOT(rdl,f'PATH{key}/nRDL')
        u=self.net.AND(nrdl,u0,f'PATH{key}/Upri')
        b0=self.net.OR(d,u,f'PATH{key}/b0');b1=self.net.OR(l,u,f'PATH{key}/b1')
        v=Value('byte',[b0,b1]+[GND]*6,ready=ready);self._path_value[key]=v;return v
    def compile(self):
        for gi,g in enumerate(self.groups):
            self.state_index=gi;self.env={n:self.varval(n) for n in self.vars};start={n:v.bits[:] for n,v in self.env.items()};self.vr_slot=0;self.ram_read_key=None;self.vram_read_key=None;self.alu_used=False
            terminal=None
            for oi,o in g:
                if o.kind=='decl':
                    v=self.cbyte(0) if o.data.init is None else self.expr(o.data.init);self.env[o.data.name]=v
                elif o.kind=='assign':
                    cur=self.getvar(o.data.name);v=self.expr(o.data.expr)
                    if o.data.op=='+=':v=Value('byte',self.alu_or_fallback(cur.bits,v.bits,'ADD',f'S{gi}/addas'))
                    elif o.data.op=='-=':v=Value('byte',self.alu_or_fallback(cur.bits,v.bits,'SUB',f'S{gi}/subas'))
                    self.env[o.data.name]=v
                elif o.kind=='incdec':
                    cur=self.getvar(o.data.name);one=self.cbyte(1)
                    self.env[o.data.name]=Value('byte',self.alu_or_fallback(cur.bits,one.bits,'ADD',f'S{gi}/inc')) if o.data.delta>0 else Value('byte',self.alu_or_fallback(cur.bits,one.bits,'SUB',f'S{gi}/dec'))
                elif o.kind=='pure_if':self.env=self.symbolic_pure(o.data,self.env)
                elif o.kind=='call':self.call_stmt(o.data)
                elif o.kind in ('brfalse','goto','return','halt'):terminal=o
                elif o.kind=='nop':pass
                else:raise RuntimeError(o.kind)
            en=self.state[gi]
            readies=[]
            for n,vi in self.vars.items():
                v=self.env.get(n,self.varval(n))
                if v.bits!=start[n]:
                    if v.ready is not None:readies.append(v.ready)
            ready=self.net.ands(readies,f'S{gi}/ready') if readies else VCC
            act=en if ready==VCC else self.net.AND(en,ready,f'S{gi}/act')
            for n,vi in self.vars.items():
                v=self.env.get(n,self.varval(n))
                if v.bits!=start[n]:
                    self.net.term(REG_WE[vi.reg],act);self.net.route_bits(REG_DATA[vi.reg],act,v.bits,f'S{gi}/REG/{n}')
            def target_group(old):
                if old>=len(self.oldops):return len(self.groups)-1
                return self.old2g[old]
            def redirect(t):
                seen=set()
                while t not in seen and 0<=t<len(self.groups):
                    seen.add(t);gg=self.groups[t]
                    if len(gg)==1 and gg[0][1].kind=='goto':
                        t=target_group(gg[0][1].data);continue
                    break
                return t
            cases=[]
            if terminal is None:
                t=redirect(min(gi+1,len(self.groups)-1));cases=[(act,t)]
            elif terminal.kind=='goto':cases=[(act,redirect(target_group(terminal.data)))]
            elif terminal.kind=='brfalse':
                c=self.boolsig(self.expr(terminal.data[0]));nc=self.net.NOT(c,f'S{gi}/brn')
                cases=[(self.net.AND(act,nc,f'S{gi}/false'),redirect(target_group(terminal.data[1]))),(self.net.AND(act,c,f'S{gi}/true'),redirect(min(gi+1,len(self.groups)-1)))]
                self.net.term(BR1,self.net.AND(act,c,f'S{gi}/B1'));self.net.term(BR2,self.net.AND(act,nc,f'S{gi}/B2'))
            else:cases=[(act,gi)]
            if ready!=VCC:
                nr=self.net.NOT(ready,f'S{gi}/nready');cases.append((self.net.AND(en,nr,f'S{gi}/stall'),gi))
            for cond,t in cases:
                for b in range(7):
                    if (t>>b)&1:self.net.term(PC_D[b],cond)
        self.net.finish();return self.net.parts

def normalize_base(level):
    level=copy.deepcopy(level);parts=[]
    for p in level['parts']:
        z=parse_custom(p)
        if z and z[0].get('uid','').startswith('ROM/'):continue
        parts.append(p)
    level['parts']=parts
    byuid={}
    for p in level['parts']:
        z=parse_custom(p)
        if z:byuid[z[0]['uid']]=(p,z[0],z[1])
    def patch(uid,**kw):
        p,m,d=byuid[uid];d.update(kw);m['data']=json.dumps(d,separators=(',',':'));p['team']=json.dumps(m,separators=(',',':'))
    for b in range(5):patch(f'gs8v12-{6998+b:05d}-PC{b}',d=PC_D[b])
    level['parts'].append(custom_part(52,103,'HW/NATIVE/PC5','DFF',{'d':PC_D[5],'clock':CPUCLK,'q':PCQ[5],'notQ':PCN[5],'initialQ':0}))
    level['parts'].append(custom_part(53,103,'HW/NATIVE/PC6','DFF',{'d':PC_D[6],'clock':CPUCLK,'q':PCQ[6],'notQ':PCN[6],'initialQ':0}))
    gi=0
    for r in range(8):
        nwe=930000+r
        level['parts'].append(custom_part(55+r,101,'HW/NATIVE/R%d/NWE'%r,'NOT',{'input':REG_WE[r],'output':nwe}))
        for b in range(8):
            q=REGQ[r][b];new=930100+r*32+b*3;hold=new+1;dd=new+2
            level['parts'].append(custom_part(56+r,100-b*.4,f'HW/NATIVE/R{r}B{b}/NEW','AND',{'inputA':REG_WE[r],'inputB':REG_DATA[r][b],'output':new}))
            level['parts'].append(custom_part(57+r,100-b*.4,f'HW/NATIVE/R{r}B{b}/HOLD','AND',{'inputA':nwe,'inputB':q,'output':hold}))
            level['parts'].append(custom_part(58+r,100-b*.4,f'HW/NATIVE/R{r}B{b}/D','OR',{'inputA':new,'inputB':hold,'output':dd}))
            for p in level['parts']:
                z=parse_custom(p)
                if z and z[0]['type']=='MP/Logic/DFF' and z[1].get('q')==q:
                    m,d=z;d['d']=dd;m['data']=json.dumps(d,separators=(',',':'));p['team']=json.dumps(m,separators=(',',':'));break
    patch('gs8v12-04326-RAM-WE-RAW-o0-0',inputA=RAM_WE)
    for bit,idx in [(0,4161),(1,4168),(2,4175),(4,4189)]:
        patch(f'gs8v12-{idx:05d}-RAWA-I{bit}',inputA=RAM_WE,inputB=RAM_WADDR[bit])
    level['parts'].append(custom_part(60,94,'HW/NATIVE/RAWA-I3','AND',{'inputA':RAM_WE,'inputB':RAM_WADDR[3],'output':405101}))
    for b,out in enumerate([405250,405257,405264,405271,405278,405285,405292,405299]):
        level['parts'].append(custom_part(61+b,94,f'HW/NATIVE/RWD-X{b}','AND',{'inputA':RAM_WE,'inputB':RAM_WDATA[b],'output':out}))
    pos=[401731,401746,401761,401776,401791];neg=[401965,401966,401967,401968,401969];rneg=[]
    for b in range(5):
        o=930900+b;rneg.append(o);level['parts'].append(custom_part(64+b,94,f'HW/NATIVE/RAMRN{b}','NOT',{'input':RAM_RADDR[b],'output':o}))
    for p in level['parts']:
        z=parse_custom(p)
        if not z:continue
        m,d=z;uid=m.get('uid','')
        if 'RAM-RB-' in uid:
            changed=False
            for k in ('inputA','inputB','input'):
                if k in d:
                    if d[k] in pos:d[k]=RAM_RADDR[pos.index(d[k])];changed=True
                    elif d[k] in neg:d[k]=rneg[neg.index(d[k])];changed=True
            if changed:m['data']=json.dumps(d,separators=(',',':'));p['team']=json.dumps(m,separators=(',',':'))
    patch('gs8v12-03338-BR2-EFF',output=931050)
    patch('gs8v12-05543-W0E-CLEAR1',output=931060)
    patch('gs8v12-05545-W1E-CLR0',output=931061)
    patch('gs8v12-05547-W1E-CLR1',output=931062)
    patch('gs8v12-05548-W1E-CLEAR0',output=931063)

    grc_final=[403479,403482,403485,403488,403491,403494,403497,403500]
    for b,out in enumerate(grc_final):
        level['parts'].append(custom_part(72+b,94,f'HW/NATIVE/VRADDR-DIRECT{b}','AND',
            {'inputA':VCC,'inputB':VR_RADDR[b],'output':out}))
    patch('gs8v12-02940-GR-P0',inputA=VR_P0)
    level['parts'].append(custom_part(81,94,'HW/NATIVE/GR-P1','AND',{'inputA':VR_P1,'inputB':403858,'output':403860}))
    return level

def compile_source(src):
    body=Parser(lex(src.read_text())).parse();ir=NativeIRBuilder();ops=ir.finalize(body);cc=NativeCompiler(src.stem,ops);rom=cc.compile()
    return rom,len(cc.groups),len(rom),{n:v.reg for n,v in cc.vars.items()}

def validate_rom_abi(level):
    owned=[406457,406458,406459,406460,406461,406463,406464,406465,406466,406468]
    producers={x:[] for x in owned}
    for p in level['parts']:
        z=parse_custom(p)
        if not z:continue
        m,d=z
        o=d.get('output')
        if o in producers:producers[o].append(m.get('uid',''))
    bad={k:v for k,v in producers.items() if len(v)>1}
    if bad:raise RuntimeError('duplicate GPU control producer(s): '+repr(bad))

def validate_gpu_read_address_bus(level):
    finals=[403479,403482,403485,403488,403491,403494,403497,403500]
    producers={x:[] for x in finals}
    for p in level['parts']:
        z=parse_custom(p)
        if not z:continue
        m,d=z;o=d.get('output')
        if o in producers:producers[o].append((m.get('uid',''),d))
    for i,out in enumerate(finals):
        ps=producers[out]
        if len(ps)!=1:
            raise RuntimeError(f'GPU read address bit {i} ({out}) has {len(ps)} producers')
        uid,d=ps[0]
        if d.get('inputA')!=VCC or d.get('inputB')!=VR_RADDR[i]:
            raise RuntimeError(f'GPU read address bit {i} is not wired to VR_RADDR[{i}]: {uid} {d}')

def build_one(src,base,out):
    lv=decode_level(base);rom,states,gates,regs=compile_source(src);before=hw_hash(lv)
    lv['parts']=[p for p in lv['parts'] if not (parse_custom(p) and parse_custom(p)[0].get('uid','').startswith('ROM/'))]
    insert=min(756,len(lv['parts']));lv['parts']=lv['parts'][:insert]+rom+lv['parts'][insert:]
    assert hw_hash(lv)==before
    validate_rom_abi(lv)
    validate_gpu_read_address_bus(lv)
    code=encode_level(lv);rt=json.loads(zlib.decompress(base64.b64decode(code),-15).decode());assert rt==lv
    out.parent.mkdir(parents=True,exist_ok=True);out.write_text(code)
    print(f'{src.name}: {states} wide states, {gates} ROM gates, {len(code)} bytes -> {out.name}')
    print('  regs:',', '.join(f'{k}=R{v}' for k,v in regs.items()))
    return {'states':states,'rom_gates':gates,'bytes':len(code),'registers':regs}

def main():
    ap=argparse.ArgumentParser(description='C# -> Gunsaw level')
    ap.add_argument('programs',nargs='*',help='program names')
    a=ap.parse_args()
    root=pathlib.Path(__file__).resolve().parent;base=root/'base.txt'
    names=a.programs or [p.stem for p in sorted((root/'src').glob('*.cs'))]
    for n in names:
        p=pathlib.Path(n)
        src=(root/'src'/p.name) if p.suffix=='.cs' else (root/'src'/f'{n}.cs')
        if not src.exists():raise SystemExit(f'program not found: {src}')
        build_one(src,base,root/'out'/f'{src.stem}.txt')
if __name__=='__main__':main()
