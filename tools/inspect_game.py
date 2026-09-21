"""Read-only PE/x86 inspection; offsets printed as RVAs, never live addresses."""
import argparse
import hashlib
import json
import pathlib
import struct
import sys
sys.path.insert(0,str(pathlib.Path(__file__).resolve().parents[1]/'.deps/python'))
import pefile
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

p=argparse.ArgumentParser()
p.add_argument('exe',type=pathlib.Path)
p.add_argument('--string')
p.add_argument('--rva',type=lambda x:int(x,0))
p.add_argument('--length',type=lambda x:int(x,0),default=256)
a=p.parse_args()
data=a.exe.read_bytes()
pe=pefile.PE(data=data)
base=pe.OPTIONAL_HEADER.ImageBase
text=next(s for s in pe.sections if s.Name.rstrip(b'\0')==b'.text')
md=Cs(CS_ARCH_X86,CS_MODE_32)
if a.rva is not None:
    for i in md.disasm(pe.get_data(a.rva,a.length),a.rva):
        print(f'{i.address:08x}  {i.bytes.hex():<22} {i.mnemonic:<8} {i.op_str}')
elif a.string:
    needle=a.string.encode()+b'\0'
    pos=0
    while (pos:=data.find(needle,pos))!=-1:
        rva=pe.get_rva_from_offset(pos)
        address=struct.pack('<I',base+rva)
        print(f'string rva={rva:#x}')
        code=text.get_data()
        start=0
        while (start:=code.find(address,start))!=-1:
            ref=text.VirtualAddress+start
            print(f'  possible xref operand rva={ref:#x}')
            start+=1
        pos+=1
else:
    print(json.dumps({'sha256':hashlib.sha256(data).hexdigest(),'machine':hex(pe.FILE_HEADER.Machine),
        'image_base':hex(base),'image_size':hex(pe.OPTIONAL_HEADER.SizeOfImage),
        'large_address_aware':bool(pe.FILE_HEADER.Characteristics&32),
        'sections':[{'name':s.Name.rstrip(b'\0').decode(),'rva':hex(s.VirtualAddress),
                     'virtual_size':s.Misc_VirtualSize} for s in pe.sections]},indent=2))
