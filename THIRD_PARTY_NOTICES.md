# Third-party components and research

The Windows agent uses [NAudio.Wasapi and NAudio.Core 2.2.1](https://github.com/naudio/NAudio/tree/v2.2.1), distributed under the MIT license. Retain their license when distributing binaries. .NET and Windows SDK runtime components retain their respective licenses; self-contained publish includes runtime license notices.

NAudio license:

Copyright (c) 2020 Mark Heath

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

SmartThings SDK modules are supplied by the hub, not vendored here. The Apache-2.0 Sonos and iquix Chromecast driver sources were inspected for capability and lifecycle patterns; no implementation code was copied. Source links and findings are in [architecture.md](docs/architecture.md).

Optional development tools Lupa (MIT) and PyYAML (MIT) are not part of the agent distribution.

The Edge driver includes Egor Skriptunoff's `pure_lua_SHA` at commit
`6adac177c16c3496899f69d220dfb20bc31c03df` for SHA-256 certificate fingerprints.
Source: https://github.com/Egor-Skriptunoff/pure_lua_SHA
The original MIT license is retained in `edge-driver/SHA2-LICENSE`.
The INT64 implementation chunk is compiled statically because Edge disables
dynamic `load`; its algorithm is unchanged and checked against SHA-256 vectors.
