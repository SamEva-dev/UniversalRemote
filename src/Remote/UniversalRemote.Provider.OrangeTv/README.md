# UniversalRemote.Provider.OrangeTv

Experimental local-network provider for Orange TV UHD decoders exposing the community-observed `remoteControl/cmd` HTTP endpoint on TCP 8080.

## Safety and support boundary

- The route accepts only literal private/local IP addresses. Public hosts, URLs and loopback addresses are rejected before any request is sent.
- Commands are GET requests to the fixed path `/remoteControl/cmd` and fixed port `8080`; user input never controls the scheme, port, path or query shape.
- Pairing performs a read-only `operation=10` status probe and requires an Orange/TV decoder identity before registering the route.
- The provider is `Experimental`. The endpoint is not treated as an officially supported public Orange API.
- A command is never retried implicitly. HTTP success means accepted by the endpoint, not proof that the physical decoder changed state.
- Physical validation must record decoder model, firmware, action, date and observed result before any compatibility promotion.

Protocol evidence reviewed for REMOTE-041/042:
- https://communaute.orange.fr/t5/TV-par-ADSL-et-Fibre/API-pour-commander-le-decodeur-TV-depusi-une-tablette/m-p/520729
- https://github.com/DalFanajin/Orange-Livebox-TV-UHD-4K-python-controller
