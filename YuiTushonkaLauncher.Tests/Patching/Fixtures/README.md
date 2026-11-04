# Patch fixtures

`small.delta` is a real HDiffPatch delta (`HDIFF13`, no compression) that turns `old.bin` (6,144 bytes) into
`new.bin` (6,240 bytes): an in-place edit, an insertion, a deletion and a growing tail.

Regenerate with the reference tool from <https://github.com/sisong/HDiffPatch>
(`make hdiffz DIR_DIFF=0 LZMA=0 ZSTD=0 MD5=0 BZIP2=0 ZLIB=0 LDEF=0 BSD=0 VCD=0`,
then `hdiffz -m-1 -f old.bin new.bin small.delta`). The files are tiny on purpose: the real SPT delta is
4 MB and is exercised by the opt-in `Live=Spt` tests instead.
