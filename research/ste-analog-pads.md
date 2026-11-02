# Do STE-era games use the enhanced controller ports and analog pads?

Status: research note, 2026-11-01. Question asked: is it worth emulating the STE's extended controller
ports with analog pads?

## What the ports are

The STE has two 15-pin high-density ports ("Enhanced Joystick Ports", EJP) that replace the ST's DE-9
ports. They keep the ST's four directions + fire, and add: a **second fire button**, **two analog-to-digital
converters per port** (8-bit, 0–255), and a **shift-register path** for Jaguar-style joypads. On the
board, the ADC and port logic are in the GSTMCU (`XPAD0X/Y`, `XPAD1X/Y`, `XPEN`), and the paddle timing
uses the LM556 (service manual, J7 paddle test: the RC network "varies the output pulse of the LM556.
The pulse width is measured by the GSTMCU chip").

## What software actually used

Two separate questions, and the evidence separates them sharply.

### Analog pads / paddles: almost nothing

The Enhanced Joystick Port FAQ (v0.90, Sept 1996 — the document written specifically to encourage EJP
use) answers this in one line, §8.4 *"Programs which will use an analogue paddle"*:

> "As far as I know, the really poor version of breakout that came free with the Falcon supports analogue
> paddles, but I don't know of anything else."

That is the whole list. Note what it says: the only title the FAQ's author could name is a **Falcon
pack-in**, not an STE game. Analog joysticks (§6) are described as a technique, with no title list at
all.

### Digital joypads (Jaguar-style): a short list, mostly Falcon and PD

§3.7 of the same FAQ lists programs that use a joypad at all — the digital path, not the analog one. It
opens by calling itself *"very sparse"*, and the entries are: PD/shareware (Double Bobble, Super Bomber
Man, BioHazard 2, Tron 2000 preview, STOS Falcon extension) and commercial (Evolution Dino Dudes,
**Llamazap** — the only one marked "compulsory", Gravon, Multi-Briques, Ping 2000, Steel Talons). Most
are tagged `?`, i.e. the author did not know whether a joypad was even supported.

An AtariAge thread on the ports is blunter: *"Nothing is transparently supported by games… I don't think
a lot of games use them."*

## Assessment: is it worth emulating?

**The ports, yes. The analog conversion, no — not as a fidelity goal.**

1. **Compatibility floor.** The ports are ordinary memory-mapped I/O (`$FF9200`/`$FF9202`), and the ST-era
   mouse and joystick paths *must* work for TOS, GEM and the walking skeleton. That is the baseline, and
   it is already in scope in the Controllers chip.
2. **Analog pads as a feature: near-zero demand.** One Falcon pack-in, by the only source that ever
   catalogued this, and nothing on the STE. The cost is not trivial either: an 8-bit ADC model, the LM556
   pulse-width timing path, and a host-side mapping from a modern analog stick to a 0–255 reading —
   all to serve software that does not exist on this machine.
3. **Digital joypads: a handful of titles, mostly Falcon-era and PD.** Worth supporting the *register
   behaviour* (the shift path at `$FF9202`) because it is cheap once the port exists, but not worth
   designing the model around.
4. **The honest recommendation:** implement `$FF9200`/`$FF9202` and the mouse/joystick scan, stub the ADC
   so reads return a defined value, and put analog pads behind a documented "not modelled" line in the
   ADR/glossary rather than in the backlog. Revisit only if a specific demo or test needs it — the same
   rule used for MIDI, which is already out of scope.

**Caveat on the evidence.** The FAQ is a 1996 enthusiast document, not an exhaustive database, and its
list is self-described as incomplete; the AtariAge comment is anecdotal. The conclusion is therefore
"no evidence of significant use", not "proof of none". Since the decision is to *defer* rather than to
*exclude*, that is a safe direction: deferring costs nothing if a title surfaces, while building an ADC
model now would cost real effort for no known consumer.
