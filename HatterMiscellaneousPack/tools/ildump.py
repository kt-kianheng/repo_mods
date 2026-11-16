#!/usr/bin/env python3
"""Minimal pure-Python .NET metadata + IL dumper (ECMA-335).
Usage:
  ildump.py DLL types REGEX
  ildump.py DLL dump TYPENAME [--il] [--method REGEX]
  ildump.py DLL refs REGEX        # methods whose IL references a member/string matching REGEX
"""
import struct, sys, re

def u16(b, o): return struct.unpack_from('<H', b, o)[0]
def u32(b, o): return struct.unpack_from('<I', b, o)[0]

CODED = {
 'TypeDefOrRef': (2, [0x02, 0x01, 0x1B]),
 'HasConstant': (2, [0x04, 0x08, 0x17]),
 'HasCustomAttribute': (5, [0x06,0x04,0x01,0x02,0x08,0x09,0x0A,0x00,0x0E,0x17,0x14,0x11,0x1A,0x1B,0x20,0x23,0x26,0x27,0x28,0x2A,0x2C,0x2B]),
 'HasFieldMarshal': (1, [0x04, 0x08]),
 'HasDeclSecurity': (2, [0x02, 0x06, 0x20]),
 'MemberRefParent': (3, [0x02, 0x01, 0x1A, 0x06, 0x1B]),
 'HasSemantics': (1, [0x14, 0x17]),
 'MethodDefOrRef': (1, [0x06, 0x0A]),
 'MemberForwarded': (1, [0x04, 0x06]),
 'Implementation': (2, [0x26, 0x23, 0x27]),
 'CustomAttributeType': (3, [None, None, 0x06, 0x0A, None]),
 'ResolutionScope': (2, [0x00, 0x1A, 0x23, 0x01]),
 'TypeOrMethodDef': (1, [0x02, 0x06]),
}
S, G, B = 'S', 'G', 'B'
def T(n): return ('T', n)
def C(n): return ('C', n)
SCHEMA = {
 0x00: [('Generation',2),('Name',S),('Mvid',G),('EncId',G),('EncBaseId',G)],
 0x01: [('Scope',C('ResolutionScope')),('Name',S),('Namespace',S)],
 0x02: [('Flags',4),('Name',S),('Namespace',S),('Extends',C('TypeDefOrRef')),('FieldList',T(0x04)),('MethodList',T(0x06))],
 0x03: [('Field',T(0x04))],
 0x04: [('Flags',2),('Name',S),('Signature',B)],
 0x05: [('Method',T(0x06))],
 0x06: [('RVA',4),('ImplFlags',2),('Flags',2),('Name',S),('Signature',B),('ParamList',T(0x08))],
 0x07: [('Param',T(0x08))],
 0x08: [('Flags',2),('Sequence',2),('Name',S)],
 0x09: [('Class',T(0x02)),('Interface',C('TypeDefOrRef'))],
 0x0A: [('Class',C('MemberRefParent')),('Name',S),('Signature',B)],
 0x0B: [('Type',2),('Parent',C('HasConstant')),('Value',B)],
 0x0C: [('Parent',C('HasCustomAttribute')),('Type',C('CustomAttributeType')),('Value',B)],
 0x0D: [('Parent',C('HasFieldMarshal')),('NativeType',B)],
 0x0E: [('Action',2),('Parent',C('HasDeclSecurity')),('PermissionSet',B)],
 0x0F: [('PackingSize',2),('ClassSize',4),('Parent',T(0x02))],
 0x10: [('Offset',4),('Field',T(0x04))],
 0x11: [('Signature',B)],
 0x12: [('Parent',T(0x02)),('EventList',T(0x14))],
 0x13: [('Event',T(0x14))],
 0x14: [('Flags',2),('Name',S),('EventType',C('TypeDefOrRef'))],
 0x15: [('Parent',T(0x02)),('PropertyList',T(0x17))],
 0x16: [('Property',T(0x17))],
 0x17: [('Flags',2),('Name',S),('Type',B)],
 0x18: [('Semantics',2),('Method',T(0x06)),('Association',C('HasSemantics'))],
 0x19: [('Class',T(0x02)),('Body',C('MethodDefOrRef')),('Decl',C('MethodDefOrRef'))],
 0x1A: [('Name',S)],
 0x1B: [('Signature',B)],
 0x1C: [('Flags',2),('Member',C('MemberForwarded')),('ImportName',S),('ImportScope',T(0x1A))],
 0x1D: [('RVA',4),('Field',T(0x04))],
 0x1E: [('Token',4),('FuncCode',4)],
 0x1F: [('Token',4)],
 0x20: [('HashAlgId',4),('Major',2),('Minor',2),('Build',2),('Rev',2),('Flags',4),('PublicKey',B),('Name',S),('Culture',S)],
 0x21: [('Processor',4)],
 0x22: [('OSPlatformId',4),('OSMajor',4),('OSMinor',4)],
 0x23: [('Major',2),('Minor',2),('Build',2),('Rev',2),('Flags',4),('PublicKey',B),('Name',S),('Culture',S),('Hash',B)],
 0x24: [('Processor',4),('AssemblyRef',T(0x23))],
 0x25: [('OSPlatformId',4),('OSMajor',4),('OSMinor',4),('AssemblyRef',T(0x23))],
 0x26: [('Flags',4),('Name',S),('Hash',B)],
 0x27: [('Flags',4),('TypeDefId',4),('Name',S),('Namespace',S),('Impl',C('Implementation'))],
 0x28: [('Offset',4),('Flags',4),('Name',S),('Impl',C('Implementation'))],
 0x29: [('Nested',T(0x02)),('Enclosing',T(0x02))],
 0x2A: [('Number',2),('Flags',2),('Owner',C('TypeOrMethodDef')),('Name',S)],
 0x2B: [('Method',C('MethodDefOrRef')),('Inst',B)],
 0x2C: [('Owner',T(0x2A)),('Constraint',C('TypeDefOrRef'))],
}

OP1 = {}
def _op(code, name, kind='none'): OP1[code] = (name, kind)
for i,n in enumerate(['nop','break','ldarg.0','ldarg.1','ldarg.2','ldarg.3','ldloc.0','ldloc.1','ldloc.2','ldloc.3','stloc.0','stloc.1','stloc.2','stloc.3']): _op(i,n)
for c,n in [(0x0E,'ldarg.s'),(0x0F,'ldarga.s'),(0x10,'starg.s'),(0x11,'ldloc.s'),(0x12,'ldloca.s'),(0x13,'stloc.s')]: _op(c,n,'u8')
_op(0x14,'ldnull'); _op(0x15,'ldc.i4.m1')
for i in range(9): _op(0x16+i,'ldc.i4.%d'%i)
_op(0x1F,'ldc.i4.s','i8'); _op(0x20,'ldc.i4','i32'); _op(0x21,'ldc.i8','i64'); _op(0x22,'ldc.r4','r4'); _op(0x23,'ldc.r8','r8')
_op(0x25,'dup'); _op(0x26,'pop'); _op(0x27,'jmp','tok'); _op(0x28,'call','tok'); _op(0x29,'calli','tok'); _op(0x2A,'ret')
brn = ['br','brfalse','brtrue','beq','bge','bgt','ble','blt','bne.un','bge.un','bgt.un','ble.un','blt.un']
for i,n in enumerate(brn): _op(0x2B+i, n+'.s','br8'); _op(0x38+i, n,'br32')
_op(0x45,'switch','switch')
for i,n in enumerate(['ldind.i1','ldind.u1','ldind.i2','ldind.u2','ldind.i4','ldind.u4','ldind.i8','ldind.i','ldind.r4','ldind.r8','ldind.ref','stind.ref','stind.i1','stind.i2','stind.i4','stind.i8','stind.r4','stind.r8','add','sub','mul','div','div.un','rem','rem.un','and','or','xor','shl','shr','shr.un','neg','not','conv.i1','conv.i2','conv.i4','conv.i8','conv.r4','conv.r8','conv.u4','conv.u8']): _op(0x46+i,n)
for c,n in [(0x6F,'callvirt'),(0x70,'cpobj'),(0x71,'ldobj'),(0x73,'newobj'),(0x74,'castclass'),(0x75,'isinst'),(0x79,'unbox'),(0x7B,'ldfld'),(0x7C,'ldflda'),(0x7D,'stfld'),(0x7E,'ldsfld'),(0x7F,'ldsflda'),(0x80,'stsfld'),(0x81,'stobj'),(0x8C,'box'),(0x8D,'newarr'),(0x8F,'ldelema'),(0xA3,'ldelem'),(0xA4,'stelem'),(0xA5,'unbox.any'),(0xC2,'refanyval'),(0xC6,'mkrefany'),(0xD0,'ldtoken')]: _op(c,n,'tok')
_op(0x72,'ldstr','str'); _op(0x76,'conv.r.un'); _op(0x7A,'throw'); _op(0x8E,'ldlen')
for c in list(range(0x82,0x8C))+list(range(0x90,0xA3))+list(range(0xB3,0xBB))+[0xC3]+list(range(0xD1,0xDD))+[0xDF,0xE0]: OP1.setdefault(c,('op_%02X'%c,'none'))
_op(0xDC,'endfinally'); _op(0xDD,'leave','br32'); _op(0xDE,'leave.s','br8')
OP2 = {0x00:('arglist','none'),0x01:('ceq','none'),0x02:('cgt','none'),0x03:('cgt.un','none'),0x04:('clt','none'),0x05:('clt.un','none'),0x06:('ldftn','tok'),0x07:('ldvirtftn','tok'),0x09:('ldarg','u16'),0x0A:('ldarga','u16'),0x0B:('starg','u16'),0x0C:('ldloc','u16'),0x0D:('ldloca','u16'),0x0E:('stloc','u16'),0x0F:('localloc','none'),0x11:('endfilter','none'),0x12:('unaligned.','u8'),0x13:('volatile.','none'),0x14:('tail.','none'),0x15:('initobj','tok'),0x16:('constrained.','tok'),0x17:('cpblk','none'),0x18:('initblk','none'),0x19:('no.','u8'),0x1A:('rethrow','none'),0x1C:('sizeof','tok'),0x1D:('refanytype','none'),0x1E:('readonly.','none')}

PRIM = {0x01:'void',0x02:'bool',0x03:'char',0x04:'sbyte',0x05:'byte',0x06:'short',0x07:'ushort',0x08:'int',0x09:'uint',0x0A:'long',0x0B:'ulong',0x0C:'float',0x0D:'double',0x0E:'string',0x16:'TypedRef',0x18:'IntPtr',0x19:'UIntPtr',0x1C:'object'}

class Asm:
    def __init__(self, path):
        self.b = open(path,'rb').read(); b = self.b
        pe = u32(b, 0x3C); nsec = u16(b, pe+6); optsz = u16(b, pe+20); opt = pe+24
        magic = u16(b, opt); dd = opt + (96 if magic == 0x10b else 112)
        cli_rva = u32(b, dd + 14*8)
        self.secs = []
        so = opt + optsz
        for i in range(nsec):
            o = so + i*40
            self.secs.append((u32(b,o+12), u32(b,o+8), u32(b,o+20)))
        cli = self.off(cli_rva)
        md = self.off(u32(b, cli+8))
        vlen = u32(b, md+12); p = md + 16 + vlen + 2; nstreams = u16(b, p); p += 2
        self.st = {}
        for _ in range(nstreams):
            off, size = u32(b,p), u32(b,p+4); p += 8
            e = b.index(b'\0', p); name = b[p:e].decode(); p = (e + 4) & ~3
            self.st[name] = (md+off, size)
        t = self.st.get('#~') or self.st['#-']
        tp = t[0]; hs = b[tp+6]
        self.sidx = 4 if hs & 1 else 2; self.gidx = 4 if hs & 2 else 2; self.bidx = 4 if hs & 4 else 2
        valid = struct.unpack_from('<Q', b, tp+8)[0]
        p = tp + 24; self.rows = {}
        for i in range(64):
            if valid >> i & 1: self.rows[i] = u32(b, p); p += 4
        if hs & 0x40: p += 4
        self.tables = {}
        for i in range(64):
            if i not in self.rows: continue
            cols = SCHEMA[i]; sizes = [self.colsize(c) for _,c in cols]; rs = sum(sizes)
            self.tables[i] = (p, rs, cols, sizes); p += rs * self.rows[i]
        self._cache = {}
        self.nested = {}
        for r in range(1, self.rows.get(0x29,0)+1):
            n = self.row(0x29, r); self.nested[n['Nested']] = n['Enclosing']

    def off(self, rva):
        for va, vs, raw in self.secs:
            if va <= rva < va + max(vs,1): return rva - va + raw
        raise ValueError('rva %x' % rva)
    def colsize(self, c):
        if c == S: return self.sidx
        if c == G: return self.gidx
        if c == B: return self.bidx
        if isinstance(c, int): return c
        if c[0] == 'T': return 2 if self.rows.get(c[1],0) < 65536 else 4
        bits, tabs = CODED[c[1]]
        mx = max(self.rows.get(t,0) for t in tabs if t is not None)
        return 2 if mx < (1 << (16-bits)) else 4
    def row(self, t, i):
        key = (t,i)
        if key in self._cache: return self._cache[key]
        p, rs, cols, sizes = self.tables[t]; o = p + (i-1)*rs; d = {}
        for (n,c),s in zip(cols,sizes):
            v = u16(self.b,o) if s == 2 else u32(self.b,o); o += s
            if c == S: v = self.str(v)
            elif isinstance(c, tuple) and c[0] == 'C':
                bits, tabs = CODED[c[1]]; v = (tabs[v & ((1<<bits)-1)], v >> bits)
            d[n] = v
        self._cache[key] = d; return d
    def str(self, i):
        o = self.st['#Strings'][0] + i; e = self.b.index(b'\0', o)
        return self.b[o:e].decode('utf-8','replace')
    def blob(self, i):
        o = self.st['#Blob'][0] + i; n, o = self.cint(o); return self.b[o:o+n]
    def us(self, i):
        o = self.st['#US'][0] + i; n, o = self.cint(o)
        return self.b[o:o+n-1].decode('utf-16-le','replace') if n else ''
    def cint(self, o, buf=None):
        b = buf if buf is not None else self.b; x = b[o]
        if x & 0x80 == 0: return x, o+1
        if x & 0xC0 == 0x80: return ((x&0x3F)<<8)|b[o+1], o+2
        return ((x&0x1F)<<24)|(b[o+1]<<16)|(b[o+2]<<8)|b[o+3], o+4

    def tname(self, tab, idx):
        if idx == 0: return '?'
        if tab == 0x02:
            r = self.row(0x02, idx); n = r['Name']
            if idx in self.nested: return self.tname(0x02, self.nested[idx]) + '/' + n
            return (r['Namespace']+'.' if r['Namespace'] else '') + n
        if tab == 0x01:
            r = self.row(0x01, idx); n = r['Name']; sc = r['Scope']
            if sc[0] == 0x01: return self.tname(0x01, sc[1]) + '/' + n
            return (r['Namespace']+'.' if r['Namespace'] else '') + n
        if tab == 0x1B:
            return self.sigtype(self.blob(self.row(0x1B, idx)['Signature']), 0)[0]
        return '?%x:%d' % (tab, idx)

    def sigtype(self, s, o):
        e = s[o]; o += 1
        if e in PRIM: return PRIM[e], o
        if e in (0x11, 0x12):
            v, o = self.cint(o, s); tab = [0x02,0x01,0x1B][v & 3]; return self.tname(tab, v >> 2), o
        if e == 0x0F: t,o = self.sigtype(s,o); return t+'*', o
        if e == 0x10: t,o = self.sigtype(s,o); return 'ref '+t, o
        if e == 0x1D: t,o = self.sigtype(s,o); return t+'[]', o
        if e == 0x13: v,o = self.cint(o,s); return '!%d'%v, o
        if e == 0x1E: v,o = self.cint(o,s); return '!!%d'%v, o
        if e == 0x15:
            t,o = self.sigtype(s,o); n,o = self.cint(o,s); args=[]
            for _ in range(n): a,o = self.sigtype(s,o); args.append(a)
            return '%s<%s>' % (t.split('`')[0], ','.join(args)), o
        if e == 0x14:
            t,o = self.sigtype(s,o); rank,o = self.cint(o,s); ns,o = self.cint(o,s)
            for _ in range(ns): _,o = self.cint(o,s)
            nl,o = self.cint(o,s)
            for _ in range(nl): _,o = self.cint(o,s)
            return t+'['+','*(rank-1)+']', o
        if e in (0x1F,0x20): _,o = self.cint(o,s); return self.sigtype(s,o)
        if e == 0x45: return self.sigtype(s,o)
        if e == 0x41: return self.sigtype(s,o)
        if e == 0x1B: return 'fnptr', len(s)
        return '?%02x'%e, o

    def methsig(self, s):
        o = 0; cc = s[o]; o += 1
        if cc & 0x10: _,o = self.cint(o,s)
        n,o = self.cint(o,s); ret,o = self.sigtype(s,o); ps=[]
        for _ in range(n):
            if o < len(s) and s[o] == 0x41: o+=1
            p,o = self.sigtype(s,o); ps.append(p)
        return ret, ps, bool(cc & 0x20)

    def fieldsig(self, s): return self.sigtype(s, 1)[0]

    def type_range(self, t, tab, col):
        start = self.row(0x02, t)[col]
        end = self.row(0x02, t+1)[col] if t < self.rows[0x02] else self.rows[tab]+1
        return range(start, end)

    def find_type(self, name):
        res = []
        for t in range(1, self.rows[0x02]+1):
            if self.tname(0x02, t) == name: res.append(t)
        return res

    def method_owner(self, m):
        # binary search
        lo, hi = 1, self.rows[0x02]
        while lo < hi:
            mid = (lo+hi+1)//2
            if self.row(0x02, mid)['MethodList'] <= m: lo = mid
            else: hi = mid-1
        return lo
    def field_owner(self, f):
        lo, hi = 1, self.rows[0x02]
        while lo < hi:
            mid = (lo+hi+1)//2
            if self.row(0x02, mid)['FieldList'] <= f: lo = mid
            else: hi = mid-1
        return lo

    def tok(self, tk):
        tab, idx = tk >> 24, tk & 0xFFFFFF
        try:
            if tab == 0x06:
                return self.tname(0x02, self.method_owner(idx)) + '::' + self.row(0x06, idx)['Name'] + '()'
            if tab == 0x04:
                return self.tname(0x02, self.field_owner(idx)) + '::' + self.row(0x04, idx)['Name']
            if tab == 0x0A:
                r = self.row(0x0A, idx); par = r['Class']
                pn = self.tname(par[0], par[1]) if par[0] in (0x01,0x02,0x1B) else ('?' if par[0] != 0x06 else self.tok(0x06000000|par[1]))
                sig = self.blob(r['Signature'])
                suffix = '' if sig and sig[0] == 0x06 else '()'
                return pn + '::' + r['Name'] + suffix
            if tab in (0x01, 0x02, 0x1B): return self.tname(tab, idx)
            if tab == 0x2B:
                r = self.row(0x2B, idx); m = r['Method']
                return self.tok((m[0]<<24)|m[1]) + '<generic>'
            if tab == 0x70: return repr(self.us(idx))
        except Exception as ex:
            return '<tok %08x err %s>' % (tk, ex)
        return '<tok %08x>' % tk

    def il(self, m):
        r = self.row(0x06, m)
        if not r['RVA']: return None
        o = self.off(r['RVA']); h = self.b[o]
        if h & 3 == 2: size = h >> 2; o += 1
        else:
            hs = (u16(self.b,o) >> 12) * 4; size = u32(self.b, o+4); o += hs
        code = self.b[o:o+size]; out = []; i = 0
        while i < len(code):
            st = i; op = code[i]; i += 1
            if op == 0xFE: op2 = code[i]; i += 1; name, kind = OP2.get(op2, ('fe_%02X'%op2,'none'))
            else: name, kind = OP1.get(op, ('op_%02X'%op,'none'))
            arg = ''
            if kind == 'u8': arg = str(code[i]); i += 1
            elif kind == 'i8': arg = str(struct.unpack_from('<b',code,i)[0]); i += 1
            elif kind == 'u16': arg = str(u16(code,i)); i += 2
            elif kind == 'i32': arg = str(struct.unpack_from('<i',code,i)[0]); i += 4
            elif kind == 'i64': arg = str(struct.unpack_from('<q',code,i)[0]); i += 8
            elif kind == 'r4': arg = repr(struct.unpack_from('<f',code,i)[0]); i += 4
            elif kind == 'r8': arg = repr(struct.unpack_from('<d',code,i)[0]); i += 8
            elif kind == 'tok' or kind == 'str': arg = self.tok(u32(code,i)); i += 4
            elif kind == 'br8': d = struct.unpack_from('<b',code,i)[0]; i += 1; arg = 'IL_%04x' % (i+d)
            elif kind == 'br32': d = struct.unpack_from('<i',code,i)[0]; i += 4; arg = 'IL_%04x' % (i+d)
            elif kind == 'switch':
                n = u32(code,i); i += 4; tg = [struct.unpack_from('<i',code,i+4*k)[0] for k in range(n)]; i += 4*n
                arg = ','.join('IL_%04x'%(i+t) for t in tg)
            out.append('    IL_%04x: %s %s' % (st, name, arg))
        return out

    def dump(self, t, il=False, mre=None):
        r = self.row(0x02, t); ext = r['Extends']
        print('class %s : %s' % (self.tname(0x02,t), self.tname(*ext) if ext[1] else '-'))
        for f in self.type_range(t, 0x04, 'FieldList'):
            fr = self.row(0x04, f); acc = ['?','private','famandassem','internal','protected','famorassem','public','?'][fr['Flags']&7]
            st = ' static' if fr['Flags'] & 0x10 else ''
            print('  field %s%s %s %s' % (acc, st, self.fieldsig(self.blob(fr['Signature'])), fr['Name']))
        for m in self.type_range(t, 0x06, 'MethodList'):
            mr = self.row(0x06, m)
            if mre and not re.search(mre, mr['Name']): continue
            ret, ps, inst = self.methsig(self.blob(mr['Signature']))
            pr = self.type_range_params(m)
            names = [self.row(0x08,p)['Name'] for p in pr if self.row(0x08,p)['Sequence'] > 0]
            args = ', '.join('%s %s' % (a, names[k] if k < len(names) else '') for k,a in enumerate(ps))
            acc = ['?','private','famandassem','internal','protected','famorassem','public','?'][mr['Flags']&7]
            print('  method %s%s %s %s(%s)' % (acc, '' if inst else ' static', ret, mr['Name'], args))
            if il:
                code = self.il(m)
                if code: print('\n'.join(code))
    def type_range_params(self, m):
        start = self.row(0x06, m)['ParamList']
        end = self.row(0x06, m+1)['ParamList'] if m < self.rows[0x06] else self.rows.get(0x08,0)+1
        return range(start, end)

if __name__ == '__main__':
    a = Asm(sys.argv[1]); cmd = sys.argv[2]
    if cmd == 'types':
        rx = re.compile(sys.argv[3])
        for t in range(1, a.rows[0x02]+1):
            n = a.tname(0x02, t)
            if rx.search(n): print(n)
    elif cmd == 'dump':
        il = '--il' in sys.argv; mre = None
        if '--method' in sys.argv: mre = sys.argv[sys.argv.index('--method')+1]
        for t in a.find_type(sys.argv[3]): a.dump(t, il, mre)
    elif cmd == 'refs':
        rx = re.compile(sys.argv[3])
        for m in range(1, a.rows[0x06]+1):
            try: code = a.il(m)
            except Exception: continue
            if not code: continue
            hits = [l for l in code if rx.search(l)]
            if hits:
                print(a.tok(0x06000000|m))
                for h in hits[:6]: print('   ', h.strip())
