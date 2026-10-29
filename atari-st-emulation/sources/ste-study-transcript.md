# Atari STE hardware study — source transcript

> Verbatim record of a multi-day ChatGPT session (2026-10-27 → 2026-10-29) used as the raw
> material for [02-atari-ste-hardware-overview-for-emulation.md](../02-atari-ste-hardware-overview-for-emulation.md).
> Kept unedited apart from removal of inline citation markers and demotion of the original heading
> levels by two, so that claims in the overview can be traced back to the discussion they came from.
> Not a curated document: expect repetition, dead ends, and the occasional statement that the
> overview corrects or qualifies.

Share link: https://chatgpt.com/share/6ae351d2-9d9c-83eb-a99f-da2564c66523

---

## Turn 01 — 2026-10-27 21:29 UTC

### Question

> i am studying the 68000 processor, what is the frame pointer

### Answer

In the **Motorola 68000 (68000) processor**, the **frame pointer** is a register used to access local variables, function parameters, and other data belonging to a function.

Let's break it down simply.

#### 1. What is a frame pointer?

When a function is called, it often needs some memory to store its parameters, local variables, and return information. This area of memory is called a **stack frame**.

The frame pointer is a register that provides a relatively fixed reference point for accessing data in that stack frame.

On the 68000, **A6 is conventionally used as the frame pointer**.

- **A7** = Stack Pointer (SP), which points to the top of the stack.
- **A6** = Frame Pointer (FP), which points to a fixed location within the current function's stack frame.

#### 2. Example of a stack frame

Imagine a function is called with two parameters and has one local variable.

<box border radius="lg" padding={3} gap={1}>
  <text color="secondary" size="sm" textAlign="center">Higher memory addresses</text>
  <box background="surface-secondary" radius="md" padding={2}>
    **8(A6)** — Parameter 2
  </box>
  <box background="surface-secondary" radius="md" padding={2}>
    **4(A6)** — Parameter 1
  </box>
  <box background="rgba(59,130,246,0.13)" radius="md" padding={2}>
    **0(A6)** — Saved frame pointer
  </box>
  <box background="surface" border radius="md" padding={2}>
    **−4(A6)** — Local variable
  </box>
  <text color="secondary" size="sm" textAlign="center">Lower memory addresses</text>
</box>

This is a simplified example of a common 68000 stack-frame layout. The exact offsets depend on the calling convention and how the compiler arranges the frame.

Notice that the frame pointer lets the program access data using fixed offsets, such as `4(A6)` or `-4(A6)`.

#### 3. How does the 68000 use A6 as a frame pointer?

A function might begin with these instructions:

```asm
LINK A6,#-4
```

This instruction does two important things:

1. Saves the old value of A6 on the stack.
2. Sets A6 to the current stack position and reserves 4 bytes for local variables.

The function can then access its local variable using:

```asm
MOVE.W D0,-4(A6)
```

This stores the word in data register D0 at the memory address four bytes below the frame pointer.

When the function finishes, it can execute:

```asm
UNLK A6
RTS
```

- `UNLK A6` restores the previous frame pointer and releases the function's stack frame.
- `RTS` returns to the calling function.

#### 4. Why do we need a frame pointer?

The stack pointer (A7) can move as values are pushed onto or removed from the stack. This can make accessing a particular local variable inconvenient if you use only A7.

The frame pointer (A6), by contrast, normally stays fixed throughout the function, so variables and parameters can be accessed at consistent offsets.

| Register | Purpose |
|---|---|
| A7 | Tracks the current stack position |
| A6 | Provides a stable reference to the current stack frame |
| A0–A5 | Other address registers, available for general use |

**Remember for your exam:** A6 is conventionally the frame pointer on the 68000, A7 is the stack pointer, and `LINK`/`UNLK` are commonly used to create and remove stack frames.

---

## Turn 02 — 2026-10-27 21:35 UTC

### Question

> what does autovectoring mean in : 8 external interrupt levels (IPL0-IPL2, with autovectoring for levels 1-7)

### Answer

On the **Motorola 68000 processor**, *autovectoring* means that the processor automatically selects the correct interrupt service routine (ISR) based on the interrupt level, without the external device having to supply a specific interrupt vector number.

Let's break it down.

#### 1. First, what are IPL0–IPL2?

The 68000 has three **Interrupt Priority Level (IPL)** input pins:

- IPL0
- IPL1
- IPL2

Together, these pins encode the interrupt priority level from **0 to 7**.

| Interrupt level | Meaning |
|---|---|
| 0 | No interrupt requested |
| 1–6 | Maskable interrupts, with increasing priority |
| 7 | Highest priority; non-maskable interrupt |

The processor uses these signals to determine whether an interrupt should be accepted, based on its current interrupt mask.

#### 2. What does autovectoring do?

When an interrupt is accepted, the processor needs to know **where the interrupt-handling code is located**. It gets this address from the interrupt vector table.

There are two main ways to determine the vector:

<box border radius="lg" padding={3} gap={3}>
  <box gap={1}>
    <title size="md">Without autovectoring</title>
    The external device supplies a vector number during the interrupt acknowledge cycle. The processor uses that number to find the appropriate handler in the vector table.
  </box>
  <divider color="subtle"/>
  <box gap={1}>
    <title size="md">With autovectoring</title>
    The processor automatically generates the vector number from the interrupt level. The external device does not need to supply a vector number.
  </box>
</box>

For the 68000, the autovector numbers for interrupt levels 1–7 are:

| Interrupt level | Autovector number | Vector table address |
|---:|---:|---:|
| 1 | 25 | `0x064` |
| 2 | 26 | `0x068` |
| 3 | 27 | `0x06C` |
| 4 | 28 | `0x070` |
| 5 | 29 | `0x074` |
| 6 | 30 | `0x078` |
| 7 | 31 | `0x07C` |

Each vector-table entry contains the address of the corresponding interrupt service routine.

#### 3. Example

Imagine a keyboard generates an interrupt at **level 3**.

1. The keyboard or interrupt controller signals level 3 using the IPL pins.
2. The 68000 checks whether level 3 is allowed by its interrupt mask.
3. During the interrupt acknowledge cycle, the system indicates that autovectoring should be used.
4. The processor selects **autovector 27**, located at address `0x06C`.
5. The processor loads the handler's address from that location and executes the interrupt service routine.

The key point is that the device does not need to provide a custom vector number.

#### 4. Why does the specification say “8 external interrupt levels”?

Because the three IPL pins can represent \(2^3 = 8\) combinations, corresponding to levels 0–7.

Autovectoring for levels 1–7 means the processor has a standard interrupt vector available for each of those seven interrupt levels.

**One important detail:** Autovectoring is not completely automatic just because an interrupt occurs. On the original 68000, the system must indicate autovectoring during the interrupt acknowledge cycle. The processor then chooses the vector based on the interrupt level.

##### Exam tip

Remember this distinction:

- **Interrupt level:** tells the CPU the priority of the interrupt.
- **Vector number:** identifies an entry in the interrupt vector table.
- **Autovectoring:** lets the CPU derive the vector number from the interrupt level rather than receiving a device-specific vector number.

---

## Turn 03 — 2026-10-27 21:41 UTC

### Question

> tell me all about the interrupt cycle and masking and isr execution

### Answer

### Motorola 68000: Interrupts, masking, interrupt cycles, and ISR execution

The easiest way to understand interrupts on the 68000 is to follow what happens when an external device requests attention, how the CPU decides whether to accept it, and how it executes the interrupt service routine.

There are five main concepts to understand:

1. **Interrupt request** — a device asks the CPU for attention.
2. **Interrupt masking** — the CPU decides whether to accept the request.
3. **Interrupt acknowledge cycle** — the CPU identifies the interrupt vector.
4. **Exception processing** — the CPU saves the necessary state and transfers control to the handler.
5. **ISR execution and return** — the handler services the device and returns to the interrupted program.

Let's go through them in the order they happen on the original Motorola 68000.

#### 1. The big picture

<box border radius="xl" padding={3} align="center" gap={1}>
  <box background="surface-secondary" radius="lg" padding={3} width="100%" align="center">
    **1. Normal program runs**

    <text color="secondary" size="sm">CPU executes instructions</text>
  </box>
  <icon name="arrow-down" size="lg" color="secondary"/>
  <box background="surface" border radius="lg" padding={3} width="100%" align="center">
    **2. External device requests an interrupt**

    <text color="secondary" size="sm">IPL2–IPL0 encode a level from 1 to 7</text>
  </box>
  <icon name="arrow-down" size="lg" color="secondary"/>
  <box background="surface" border radius="lg" padding={3} width="100%" align="center">
    **3. CPU checks the interrupt mask**

    <text color="secondary" size="sm">Accept it if the level is higher than the mask, or if it is level 7</text>
  </box>
  <icon name="arrow-down" size="lg" color="secondary"/>
  <box background="surface" border radius="lg" padding={3} width="100%" align="center">
    **4. Interrupt exception processing**

    <text color="secondary" size="sm">Acknowledge, select vector, save state, change CPU state</text>
  </box>
  <icon name="arrow-down" size="lg" color="secondary"/>
  <box background="rgba(59,130,246,0.12)" radius="lg" padding={3} width="100%" align="center">
    **5. Execute the ISR**

    <text color="secondary" size="sm">Service the device and clear or acknowledge its request</text>
  </box>
  <icon name="arrow-down" size="lg" color="secondary"/>
  <box background="surface-secondary" radius="lg" padding={3} width="100%" align="center">
    **6. RTE instruction**

    <text color="secondary" size="sm">Restore saved processor state and resume interrupted code</text>
  </box>
</box>

One important distinction: **the interrupt request does not directly jump to the ISR.** The processor must first decide to accept the request and perform its interrupt exception sequence.

#### 2. Interrupt levels and the IPL pins

The 68000 has three active-low interrupt-priority input pins: `IPL2`, `IPL1`, and `IPL0`. Together they encode an interrupt level from 0 to 7.

| Interrupt level | Binary value (IPL2–IPL0) | Meaning |
|---:|:---:|---|
| 0 | `111` | No interrupt requested |
| 1 | `110` | Lowest maskable interrupt |
| 2 | `101` | Higher priority |
| 3 | `100` | Higher priority |
| 4 | `011` | Higher priority |
| 5 | `010` | Higher priority |
| 6 | `001` | Highest maskable interrupt |
| 7 | `000` | Special, non-maskable interrupt |

The binary patterns shown are the physical pin levels: `0` means a pin is asserted low, and `1` means it is high. That is why level 0 corresponds to `111`, while level 7 corresponds to `000`.

Levels 1–7 are prioritized in ascending order. For example, level 5 has higher priority than level 3.

#### 3. Interrupt masking: how the CPU decides

The 68000 stores its interrupt mask in the **Status Register (SR)**.

The SR contains, among other things:

- **T bit** — trace control.
- **S bit** — supervisor/user mode.
- **Interrupt mask bits I2–I0** — the interrupt priority mask.
- **Condition codes** — carry, overflow, zero, negative, and extend.

The three interrupt mask bits represent a number from 0 to 7.

The normal acceptance rule for levels 1–6 is:

\[
\boxed{\text{Accept interrupt if its level} > \text{SR mask}}
\]

An interrupt whose level is equal to or below the mask is blocked.

For example, if the interrupt mask is 3:

| Incoming level | Accepted? | Reason |
|---:|:---:|---|
| 1 | No | \(1 \leq 3\) |
| 2 | No | \(2 \leq 3\) |
| 3 | No | Equal to mask |
| 4 | Yes | \(4 > 3\) |
| 5 | Yes | \(5 > 3\) |
| 6 | Yes | \(6 > 3\) |
| 7 | Special case | Level 7 is not blocked by the mask |

**Level 7 is special:** it is not blocked by the interrupt mask. Its edge-sensitive behavior is also important: the original 68000 recognizes a level-7 interrupt when the level-7 request transitions from not asserted to asserted. A continuously asserted level 7 does not repeatedly generate interrupts simply because the CPU returns from an ISR.

#### 4. What happens when the CPU accepts an interrupt?

Suppose a device requests **interrupt level 4**, and the SR mask is 2.

Since \(4 > 2\), the CPU accepts the interrupt request. The interrupt is processed at an instruction boundary, meaning the CPU finishes the current instruction before entering the interrupt exception sequence.

The sequence is as follows.

##### Step 1 — Finish the current instruction

The CPU completes the instruction it is executing.

For example:

```asm
ADD.W D1,D0
MOVE.W D2,D3
```

If the interrupt becomes pending while `ADD.W` is executing, the CPU completes that instruction before processing the interrupt.

The CPU does not normally stop halfway through an instruction.

##### Step 2 — Perform the interrupt acknowledge cycle

The CPU starts a special bus cycle to acknowledge the interrupt.

During this cycle, the CPU identifies the interrupt level on the address bus and gives the external hardware an opportunity to respond.

The hardware can respond in two important ways:

- **Provide a vector number:** the interrupting device or controller supplies a vector number.
- **Request autovectoring:** the external hardware signals the CPU to generate the vector number automatically.

For the original 68000, autovectoring is indicated using the `VPA` signal during the interrupt acknowledge cycle. 

##### Step 3 — Determine the interrupt vector

The CPU uses the vector number to locate the appropriate entry in its vector table.

For autovectored interrupts, the vector number is:

\[
\boxed{\text{Vector number}=24+\text{interrupt level}}
\]

For example, for interrupt level 4:

\[
24+4=28
\]

Vector 28 is at address:

\[
28\times4=112=\texttt{\$70}
\]

Each vector entry occupies four bytes because it contains a 32-bit address.

<box border radius="lg" padding={3} gap={2}>
  <text color="secondary" size="sm">Example: autovector for level 4</text>
  <row align="center" gap={2}>
    <box background="surface-secondary" radius="md" padding={2} flex="1">
      <text color="secondary" size="xs">Vector number</text>
      <title size="lg">28</title>
    </box>
    <box background="surface-secondary" radius="md" padding={2} flex="1">
      <text color="secondary" size="xs">Vector address</text>
      <title size="lg">0x070</title>
    </box>
  </row>
  <text size="sm">The 32-bit value stored at address 0x070 is the starting address of the level-4 ISR.</text>
</box>

##### Step 4 — Save the processor state

Before executing the ISR, the CPU saves the information needed to resume the interrupted program.

On the original 68000, the exception processing saves:

- The **program counter (PC)** — the address at which normal execution should resume.
- The **status register (SR)** — the previous processor status, including its interrupt mask and condition codes.

The CPU switches to **supervisor mode** if it was not already in that mode. Exception processing uses the supervisor stack, with A7 serving as the active stack pointer.

The saved information is essential: without it, the CPU would not know how to restore the interrupted program's execution state.

##### Step 5 — Update the interrupt mask

The CPU changes the interrupt mask in the SR to the level of the interrupt being serviced.

In our example, the accepted interrupt was level 4, so the mask becomes 4.

This means:

- Levels 1–4 are blocked.
- Levels 5–6 can still interrupt the ISR.
- Level 7 retains its special non-maskable behavior.

This mechanism prevents an interrupt of the same or lower priority from repeatedly interrupting the current handler, while allowing higher-priority work to be serviced.

##### Step 6 — Fetch the ISR address and execute it

The CPU reads the 32-bit address stored in the selected vector-table entry and transfers execution to that address.

The interrupt service routine now begins executing.

---

#### 5. What is an ISR?

ISR stands for **Interrupt Service Routine**. It is the code that handles a particular interrupt.

For example, a timer interrupt might require the CPU to increment a counter, while a keyboard interrupt might require it to read a character from a device register.

A simplified ISR could look like this:

```asm
TIMER_ISR:
    MOVE.W  TIMER_DATA,D0
    ADDQ.W  #1,TICK_COUNT
    ; Acknowledge or clear the timer request
    RTE
```

This is illustrative assembly; the actual device registers and acknowledgement procedure depend on the hardware.

A typical ISR should:

1. Identify and service the interrupting device.
2. Clear or acknowledge the device's interrupt request when required.
3. Preserve any registers it modifies that the interrupted program expects to retain.
4. Finish with `RTE`, not `RTS`.

The CPU's exception mechanism automatically saves the PC and SR, but it does **not** automatically save every data and address register. The ISR or its calling convention must handle those as needed.

#### 6. Why does the ISR use RTE instead of RTS?

This is a particularly important exam topic.

| Instruction | Purpose |
|---|---|
| `RTS` | Returns from a normal subroutine using the return address on the stack |
| `RTE` | Returns from an exception or interrupt, restoring the saved SR and PC |

The 68000's `RTE` instruction restores the saved processor status and program counter from the exception stack frame.

That restoration matters because the previous SR might have had a different interrupt mask, different condition codes, or a different processor mode.

For example, the mask might have been 2 before the interrupt and raised to 4 during the ISR. Returning with `RTE` restores the original mask of 2.

The CPU can then resume normal execution at the saved PC.

**Important:** `RTE` is a privileged instruction on the original 68000 because it restores the status register, including supervisor-state information.

#### 7. Can one interrupt interrupt another ISR?

Yes! This is called **nested interrupt processing**.

Suppose an interrupt at level 3 is being serviced. The CPU has raised the interrupt mask to 3.

Now a level-5 interrupt arrives.

Because \(5 > 3\), the CPU can accept the level-5 interrupt and suspend the level-3 ISR. It saves another exception frame and executes the level-5 ISR.

When the level-5 ISR executes `RTE`, the CPU restores the state of the level-3 ISR and resumes it.

<box border radius="lg" padding={3} gap={1} align="center">
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **Main program**
  </box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="rgba(59,130,246,0.12)" radius="md" padding={3} width="100%" align="center">
    **Level-3 ISR starts**
  </box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="rgba(245,158,11,0.15)" radius="md" padding={3} width="100%" align="center">
    **Level-5 interrupt arrives**

    <text size="sm">Higher priority: accepted</text>
  </box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **Level-5 ISR executes**
  </box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="surface" border radius="md" padding={3} width="100%" align="center">
    **RTE restores level-3 ISR**

    <text size="sm">The suspended handler continues</text>
  </box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **Level-3 ISR finishes, then RTE**

    <text size="sm">Main program resumes</text>
  </box>
</box>

A level-2 interrupt, by contrast, would be blocked while the mask is 3.

---

#### 8. How do you change the interrupt mask?

The interrupt mask is part of the SR. On the original 68000, modifying the SR directly is a privileged operation.

For example:

```asm
MOVE.W  #$2300,SR
```

This sets the SR to `0x2300`, which has:

- Supervisor bit `S = 1`.
- Interrupt mask `I2–I0 = 011`, so the mask is 3.
- Trace bit `T = 0`.
- Condition-code bits cleared.

This instruction also changes other SR bits, so it is not appropriate if you intend to change *only* the interrupt mask.

During an ISR, the CPU normally sets the mask to the accepted interrupt level automatically. Software can deliberately change it if its design requires a different interrupt policy.

#### 9. Putting it all together: a complete example

Imagine the main program is running with an interrupt mask of 2. A timer requests level 4, and the system uses autovectoring.

<list marker="number" gap={2}>
  <list-item>The timer asserts interrupt level 4 through the IPL pins.</list-item>
  <list-item>The CPU compares level 4 with mask 2 and accepts the request.</list-item>
  <list-item>The CPU finishes its current instruction and starts the interrupt acknowledge cycle.</list-item>
  <list-item>The hardware requests autovectoring, so the CPU selects vector 28 at address `0x070`.</list-item>
  <list-item>The CPU saves the previous SR and PC, enters supervisor mode, and sets the interrupt mask to 4.</list-item>
  <list-item>The CPU fetches the handler address from the vector-table entry and executes the timer ISR.</list-item>
  <list-item>The ISR services the timer, acknowledges its request as required, and executes `RTE`.</list-item>
  <list-item>The CPU restores the saved SR and PC, including the original mask of 2, and resumes the main program.</list-item>
</list>

The interrupt vector and its ISR address are separate things: vector 28 is the table entry number, while the 32-bit value stored at `0x070` is the address of the handler.

#### 10. Quick revision summary

| Concept | What to remember |
|---|---|
| IPL pins | Three pins encode interrupt levels 0–7 |
| Interrupt mask | Levels 1–6 are accepted only when their level exceeds the mask |
| Level 7 | Special non-maskable, edge-sensitive interrupt |
| Interrupt acknowledge | CPU identifies the interrupt vector |
| Autovector | Vector number is \(24+\text{level}\) |
| Exception entry | Save SR and PC; enter supervisor mode; update mask |
| ISR | Services the device and preserves any required registers |
| `RTE` | Restores the saved SR and PC |
| Nested interrupts | A higher-priority interrupt can interrupt a lower-priority ISR |

The most important sequence to memorize is:

**Request → Mask check → Instruction completes → Interrupt acknowledge → Save state → Fetch vector → Execute ISR → RTE → Resume program.**

For further reading, the *<Entity value="Motorola M68000 User's Manual" category="book" disambig="Original 68000 processor technical reference manual"/>* describes interrupt exception processing and vector acquisition in detail.

---

## Turn 04 — 2026-10-27 21:52 UTC

### Question

> can you detail the signals on the 68000 pins during the interruption start znd bus aknowledge cycle

### Answer

### Motorola 68000: Pin signals during interrupt entry and bus acknowledge

On the original 68000, an interrupt involves two distinct things:

1. **Interrupt detection and acceptance** — the CPU sees the interrupt level on the IPL pins and decides whether to accept it.
2. **Interrupt acknowledge bus cycle** — the CPU communicates with external hardware to obtain the interrupt vector, or signals that it will use autovectoring.

The important thing to understand is that the 68000 does not immediately jump to the ISR when the interrupt pin changes. It first completes the current instruction, then performs the interrupt exception sequence.

#### 1. The important pins

<box border radius="lg" padding={3} gap={3}>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" minWidth="76px">
      <title size="md">IPL2–0</title>
    </box>
    <box flex="1" gap={1}>
      **Interrupt Priority Level inputs**

      Three active-low inputs encode the requested interrupt level from 0 to 7. Level 0 means no interrupt request; levels 1–7 request interrupts.
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" minWidth="76px">
      <title size="md">FC2–0</title>
    </box>
    <box flex="1" gap={1}>
      **Function Code outputs**

      Indicate the type of bus access. During interrupt acknowledge, the function code identifies a CPU space cycle.
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" minWidth="76px">
      <title size="md">A1–A3</title>
    </box>
    <box flex="1" gap={1}>
      **Address outputs**

      During an interrupt acknowledge cycle, the address bus carries a special CPU-space address that identifies the interrupt level being acknowledged.
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" minWidth="76px">
      <title size="md">AS̅</title>
    </box>
    <box flex="1" gap={1}>
      **Address Strobe**

      Goes low to indicate that the address bus contains a valid address for the bus cycle.
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" minWidth="76px">
      <title size="md">UDS̅ / LDS̅</title>
    </box>
    <box flex="1" gap={1}>
      **Upper and Lower Data Strobes**

      Indicate which byte lanes are selected. Their behavior during interrupt acknowledge is special and differs from a normal memory read.
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" minWidth="76px">
      <title size="md">R/W̅</title>
    </box>
    <box flex="1" gap={1}>
      **Read/Write**

      High indicates a read cycle. Interrupt acknowledge obtains a vector from the external interrupt hardware or requests an autovector.
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" minWidth="76px">
      <title size="md">DTACK̅</title>
    </box>
    <box flex="1" gap={1}>
      **Data Transfer Acknowledge**

      External hardware asserts this to indicate completion of an ordinary bus transfer. During interrupt acknowledge, the related response can instead involve `VPA̅` for autovectoring.
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" minWidth="76px">
      <title size="md">VPA̅</title>
    </box>
    <box flex="1" gap={1}>
      **Valid Peripheral Address**

      When asserted during interrupt acknowledge, it requests autovectoring. The CPU generates the appropriate vector number internally.
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" minWidth="76px">
      <title size="md">VMA̅</title>
    </box>
    <box flex="1" gap={1}>
      **Valid Memory Address**

      Used for peripheral bus cycles with the 6800-family peripheral timing protocol. It is associated with `VPA̅`, rather than being the normal acknowledgement for a standard 68000 bus cycle.
    </box>
  </row>
</box>

The overbar means the signal is **active-low**: a logic 0 asserts the signal.

#### 2. Step A — The external device requests an interrupt

Suppose a device requests **level 3**.

The active-low IPL pins become:

| Pin | Logic level |
|---|---:|
| `IPL2` | 1 |
| `IPL1` | 0 |
| `IPL0` | 0 |

Together, `100` physically on the pins represents interrupt level 3 because the pins are active-low.

The CPU samples the request and compares level 3 against the interrupt mask in the SR.

For example:

- Mask = 2: level 3 can be accepted.
- Mask = 3: level 3 is blocked.
- Mask = 7: levels 1–6 are blocked, but level 7 remains special.

The external hardware must keep the IPL level stable long enough for the processor to recognize it. It should maintain the request until the interrupt acknowledge mechanism can respond appropriately. 

#### 3. Step B — The CPU starts the interrupt acknowledge cycle

Once the CPU accepts the request and finishes the current instruction, it begins a special bus cycle called **IACK** (interrupt acknowledge).

This is not an ordinary memory read. The function-code outputs identify the cycle as a CPU-space interrupt acknowledge operation.

For the original 68000, the key signals are:

| Signal | During IACK | Purpose |
|---|---|---|
| `FC2–FC0` | `111` | Identifies CPU space / interrupt acknowledge |
| `A3–A1` | Encodes interrupt level | Identifies which interrupt level is being acknowledged |
| `A23–A4` | All 1s | Forms part of the special CPU-space address |
| `AS̅` | Asserted low | Indicates a valid bus address |
| `R/W̅` | High | Indicates a read operation |
| `UDS̅` and `LDS̅` | Both asserted low | Enables the data bus during vector acquisition |
| `IPL2–IPL0` | Request level maintained | Keeps the interrupt level available during acknowledgement |

For level 3, the address bits `A3–A1` encode `011` because these address bits carry the **non-inverted interrupt-level number**.

The address bus is therefore not pointing to the ISR itself. It is identifying the interrupt acknowledge operation and its level. 

#### 4. Step C — The external hardware responds

There are two main ways to finish the interrupt acknowledge cycle.

<box border radius="lg" padding={3} gap={3}>
  <box gap={1}>
    <title size="lg">Case 1: Device-supplied vector</title>
    <badge color="info">DTACK̅</badge>

    The interrupt controller places an 8-bit vector number on `D7–D0` and asserts `DTACK̅` to complete the transfer.

    Example for a device using vector number 64:

    - `D7–D0 = 01000000`
    - `DTACK̅ = 0` to acknowledge the transfer
    - The CPU accepts vector number 64, which corresponds to vector-table address \(64 \times 4 = 0x100\).
  </box>
  <divider color="subtle"/>
  <box gap={1}>
    <title size="lg">Case 2: Autovector</title>
    <badge color="success">VPA̅</badge>

    External hardware asserts `VPA̅` instead of `DTACK̅`.

    The CPU generates the vector number from the interrupt level:

    \[
    \text{Vector}=24+\text{level}
    \]

    For level 3:

    \[
    24+3=27=\texttt{0x1B}
    \]

    The corresponding vector-table entry is at address:

    \[
    27\times4=\texttt{0x06C}
    \]
  </box>
</box>

On the original 68000, `DTACK̅` and `VPA̅` must not be asserted simultaneously during this operation. If neither valid response arrives, the acknowledge cycle cannot complete normally; system hardware may use a bus-error response to handle a missing or invalid interrupt source. 

#### 5. Step D — What happens to the data and control signals?

Here is the essential distinction between a normal vector response and autovectoring.

| Signal | Device-supplied vector | Autovector |
|---|---|---|
| `FC2–FC0` | `111` | `111` |
| `A3–A1` | Requested level | Requested level |
| `AS̅` | Low during active cycle | Low during active cycle |
| `R/W̅` | High (read) | High (read) |
| `UDS̅`, `LDS̅` | Both low | Both low |
| `D7–D0` | Device supplies vector number | Not used to supply the vector |
| `DTACK̅` | Asserted to finish transfer | Not used as the autovector response |
| `VPA̅` | Not asserted | Asserted to request autovectoring |

A detail that often surprises students: **both data strobes are asserted even though the vector number is only one byte.** The CPU uses the lower data bus, `D7–D0`, for the vector number; it does not use the value on `D15–D8` for that purpose. 

#### 6. Step E — After the acknowledge cycle

Completing the IACK cycle does not itself execute the ISR. The CPU continues exception processing.

The broad sequence is:

<box border radius="lg" padding={3} align="center" gap={1}>
  <box background="surface-secondary" radius="md" padding={2} width="100%" align="center">Interrupt request accepted</box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="surface-secondary" radius="md" padding={2} width="100%" align="center">Interrupt acknowledge cycle (IACK)</box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="surface-secondary" radius="md" padding={2} width="100%" align="center">Save old SR and PC on supervisor stack</box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="rgba(59,130,246,0.12)" radius="md" padding={2} width="100%" align="center">Read the selected vector-table entry</box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="surface-secondary" radius="md" padding={2} width="100%" align="center">Load PC with ISR address and begin executing it</box>
</box>

The vector-table fetch is a separate bus operation after interrupt acknowledgement. For a device-supplied vector, the vector number selects the entry; for an autovector, the CPU derives that number internally.

#### 7. A simple timing view

The original 68000 uses clock states `S0` through `S7` for a standard bus cycle. If the external hardware is not ready, wait states can extend the cycle.

A simplified view of the acknowledge phase is:

<box border radius="lg" padding={3} gap={2}>
  <row align="center" gap={2}>
    <box background="surface-secondary" radius="md" padding={2} flex="1" align="center">
      <title size="md">S0–S2</title>
      <text size="xs" textAlign="center">Cycle begins; address and function code become valid</text>
    </box>
    <icon name="arrow-right" color="secondary" size="lg"/>
    <box background="surface-secondary" radius="md" padding={2} flex="1" align="center">
      <title size="md">S3–S4</title>
      <text size="xs" textAlign="center">Address strobe and read control active</text>
    </box>
  </row>
  <row justify="center">
    <icon name="arrow-down" color="secondary" size="lg"/>
  </row>
  <box background="surface" border radius="md" padding={3} align="center">
    **S5–S7 and any wait states**

    <text size="sm" textAlign="center">External hardware supplies the vector and acknowledges, or requests autovectoring. The CPU completes the bus cycle when the response is recognized.</text>
  </box>
</box>

This is a conceptual summary, not a pin-accurate timing waveform. The exact transitions and sampling points matter when designing hardware and are shown in the official Motorola manual's **Figure 5-11, “Interrupt Acknowledge Cycle Timing Diagram.”** 

You can consult the [M68000 User's Manual from NXP](https://www.nxp.com/docs/en/reference-manual/MC68000UM.pdf), especially the sections on CPU-space cycles, interrupt control, and exception processing.

##### The key exam point

Remember that **IPL tells the CPU which interrupt level is requested; FC and the address lines identify the interrupt acknowledge cycle; and `DTACK̅` or `VPA̅` determines how that cycle completes.** The CPU then obtains the handler address from the vector table and starts the ISR.

---

## Turn 05 — 2026-10-27 22:04 UTC

### Question

> how did the atari st hardware and software interface with this interrupt system

### Answer

### How the Atari ST used the 68000 interrupt system

The Atari ST is a great real-world example of the 68000 interrupt architecture, because its hardware and operating system worked together to handle the keyboard, mouse, timers, display timing, sound, and other peripherals.

The key idea is that **the 68000 provided the interrupt mechanism, the Atari ST's hardware generated and prioritized interrupt requests, and the operating system installed routines to handle them.**

#### 1. The overall architecture

<box border radius="xl" padding={3} align="center" gap={1}>
  <box background="surface-secondary" radius="lg" padding={3} width="100%" align="center">
    **Atari ST peripherals**

    <text size="sm" textAlign="center">MFP timers, keyboard ACIA, video timing, other devices</text>
  </box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="surface" border radius="lg" padding={3} width="100%" align="center">
    **Interrupt controller hardware**

    <text size="sm" textAlign="center">MFP 68901, ACIA interrupt outputs, video-related logic</text>
  </box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="rgba(59,130,246,0.12)" radius="lg" padding={3} width="100%" align="center">
    **Motorola 68000**

    <text size="sm" textAlign="center">IPL pins → priority and mask check → interrupt acknowledge</text>
  </box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="surface" border radius="lg" padding={3} width="100%" align="center">
    **Interrupt vector table**

    <text size="sm" textAlign="center">Selects the appropriate handler address</text>
  </box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="surface-secondary" radius="lg" padding={3} width="100%" align="center">
    **TOS / GEMDOS / BIOS / device handler**

    <text size="sm" textAlign="center">Services the event, then returns with RTE</text>
  </box>
</box>

This diagram is conceptual: not every Atari ST interrupt followed exactly the same route, and not every device used the same interrupt mechanism.

#### 2. The Atari ST's three main interrupt levels

A particularly important Atari ST detail is that **the original ST uses only the 68000's interrupt levels 2, 4, and 6 for its main maskable interrupt sources**. Levels 1, 3, and 5 are not used as separate interrupt inputs in the standard configuration. Level 7 is reserved for the special non-maskable interrupt. 

| 68000 level | Atari ST source | Type of vector |
|---:|---|---|
| 6 | MFP 68901 interrupts | MFP supplies a vector |
| 4 | Vertical blank (VBL) | Autovector |
| 2 | Horizontal blank (HBL) | Autovector |
| 7 | Non-maskable interrupt | Special exception |

The MFP is especially interesting because it handles **16 internal interrupt sources**, such as timers, serial I/O, and keyboard/MIDI communication. All of these reach the 68000 at level 6, but the MFP distinguishes between them and supplies a vector for the particular source. 

This gives the Atari ST two levels of prioritization:

- The **68000** decides which interrupt levels can preempt the current program.
- The **MFP** decides which of its own 16 sources has priority.

#### 3. Example: pressing a key on the keyboard

The keyboard is a good example because it shows how the peripheral, interrupt controller, CPU, and operating system cooperate.

<list marker="number" gap={2}>
  <list-item>The keyboard's intelligent controller (IKBD) sends a keycode over a serial connection.</list-item>
  <list-item>The ACIA serial interface receives the byte and requests service.</list-item>
  <list-item>The MFP's keyboard/MIDI interrupt input handles the interrupt request, which reaches the 68000 as level 6.</list-item>
  <list-item>The 68000 accepts the interrupt if its current interrupt mask permits level 6.</list-item>
  <list-item>The MFP supplies a vector identifying the relevant interrupt source. The handler checks the ACIA status to distinguish keyboard data from MIDI data.</list-item>
  <list-item>The TOS keyboard handler processes the byte and dispatches it to the appropriate system routine or buffer.</list-item>
</list>

The key distinction is that the 68000 sees a **level-6 interrupt**, while the MFP and software determine which specific device caused it. 

#### 4. Example: vertical blank (VBL) and horizontal blank (HBL)

The Atari ST used display timing interrupts for smooth animation, palette changes, and effects synchronized with the video output.

<box gap={3}>
  <row align="start" gap={3}>
    <AsyncImage query="Atari ST monochrome color graphics display scanlines retro computer" aspectRatio="4:3" maxWidth="124px"/>
    <box flex="1" gap={1}>
      **Vertical blank — level 4**

      Occurs around the transition between frames, typically 50 times per second on a 50 Hz system. TOS uses VBL processing for tasks such as maintaining system state and running queued VBL routines. Programs could use it to update graphics-related data between frames.
      <text color="secondary" size="sm">Autovector 28 · vector address `0x070`</text>
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <AsyncImage query="Atari ST raster graphics horizontal scanline demo retro computer" aspectRatio="4:3" maxWidth="124px"/>
    <box flex="1" gap={1}>
      **Horizontal blank — level 2**

      Associated with the horizontal display timing. On the ST, the HBL autovector was used for lower-level, scanline-synchronized effects, such as changing display registers during a frame. The MFP's Timer B could also be configured for scanline-related timing.
      <text color="secondary" size="sm">Autovector 26 · vector address `0x068`</text>
    </box>
  </row>
</box>

The CPU's autovector mechanism makes these interrupts straightforward: the video-related hardware asserts the appropriate level, and the 68000 selects the corresponding vector. 

#### 5. How the software installed and managed handlers

Hardware can generate an interrupt, but software must decide what code executes.

On the Atari ST, TOS provided system vectors and interrupt routines. Programs could use operating-system interfaces to install or chain handlers, rather than replacing arbitrary memory locations without preserving the existing system behavior.

For MFP interrupts, software also configured the MFP's memory-mapped registers.

For example, the MFP registers include:

| Address | Register | Purpose |
|---|---|---|
| `0xFFFA07` | Interrupt Enable A | Enables selected MFP interrupt sources |
| `0xFFFA09` | Interrupt Enable B | Enables other MFP interrupt sources |
| `0xFFFA0D` | Interrupt Pending B | Reports pending interrupt sources |
| `0xFFFA11` | Interrupt In-Service B | Tracks sources currently being serviced |
| `0xFFFA15` | Interrupt Mask B | Masks selected MFP interrupt sources |
| `0xFFFA17` | Vector Register | Configures the MFP's interrupt-vector base |

These registers let software control individual MFP interrupt sources independently of the 68000's global interrupt mask. 

For example, disabling one MFP timer interrupt does not necessarily disable keyboard interrupts. The CPU-level mask, by contrast, can block all maskable level-6 MFP interrupts together.

#### 6. What happens when an MFP interrupt occurs?

The sequence differs slightly from a VBL autovector:

<box border radius="lg" padding={3} align="center" gap={1}>
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **Peripheral requests service**
  </box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **MFP selects a pending enabled source**
  </box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="rgba(59,130,246,0.12)" radius="md" padding={3} width="100%" align="center">
    **68000 accepts level 6**

    <text size="sm">Subject to the CPU interrupt mask</text>
  </box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **MFP provides its vector number**
  </box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **TOS or the installed handler services the source**
  </box>
  <icon name="arrow-down" color="secondary" size="lg"/>
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **Handler completes and returns with RTE**
  </box>
</box>

Unlike an autovector interrupt, the MFP interrupt uses a vector supplied by the peripheral controller. The CPU therefore does not simply calculate `24 + 6` and jump to the same handler for every MFP source. The MFP's vector distinguishes the source.

#### 7. The big lesson

The Atari ST illustrates why an interrupt system needs both hardware and software:

- The **68000** provides interrupt levels, masking, exception entry, and return.
- The **MFP 68901** manages 16 interrupt sources, their local priorities, and their vectors.
- The **video hardware** provides timing-related interrupts such as VBL and HBL.
- **TOS** initializes the hardware, maintains system handlers, and dispatches events to appropriate routines.
- **Applications and demos** can use supported system hooks or carefully managed low-level handlers for custom behavior.

One subtle point worth remembering: the 68000's interrupt mask and the MFP's own interrupt masks are two separate mechanisms. To receive a keyboard interrupt, for example, the relevant MFP source must be enabled and unmasked, and the CPU must also be willing to accept level 6.

For deeper study, *<Entity value="Atari ST Internals" category="book" disambig="Technical book on Atari ST hardware and TOS internals"/>* and the <Entity value="Atari ST Series Technical Reference Manual" category="book" disambig="Atari hardware and operating system technical documentation"/> are useful references.

---

## Turn 06 — 2026-10-27 22:16 UTC

### Question

> tell me more about the mfp 68901 features, its usage in the Atari st, how the ipl pins are shared with the video hardware (conflicts ?), and if nmi interrupts are used ?

### Answer

The Atari ST’s **MC68901 MFP (Multi-Function Peripheral)** is much more than a timer chip. It is a compact peripheral controller that handles serial communications, timing, general-purpose I/O, and a large share of the machine’s interrupt management.

The key to understanding the ST is that **the MFP has 16 interrupt sources internally, but the 68000 sees them all at CPU interrupt level 6**. Meanwhile, the video hardware generates separate interrupts at levels 2 and 4. Atari’s GLUE logic combines these signals and presents the appropriate priority level to the CPU. 

#### 1. What the MFP 68901 provides

<box gap={3}>
  <row align=start gap={3}>
    <AsyncImage query="Motorola MC68901 MFP DIP 48 integrated circuit chip" aspectRatio="4:3" width="112px" />
    <box flex="1" gap={1}>
      **16-source interrupt controller**

      It monitors 16 interrupt sources, lets software enable or mask each source, prioritizes pending requests, tracks in-service interrupts, and supplies a source-specific interrupt vector.
    </box>
  </row>
  <divider color="subtle" />
  <row align=start gap={3}>
    <AsyncImage query="Motorola 68901 MFP timer UART serial peripheral block diagram" aspectRatio="4:3" width="112px" />
    <box flex="1" gap={1}>
      **Four programmable timers**

      Timers A, B, C and D can generate periodic or event-related interrupts. Timer B is especially useful for raster effects because it can count horizontal-line events.
    </box>
  </row>
  <divider color="subtle" />
  <row align=start gap={3}>
    <AsyncImage query="Atari ST MIDI ports serial connectors rear panel" aspectRatio="4:3" width="112px" />
    <box flex="1" gap={1}>
      **Serial USART**

      Provides serial transmit and receive functions used for the ST’s RS-232 serial interface, including baud-rate-related timing and transmit/receive interrupts.
    </box>
  </row>
  <divider color="subtle" />
  <row align=start gap={3}>
    <AsyncImage query="Atari ST rear panel parallel printer port RS232 ports" aspectRatio="4:3" width="112px" />
    <box flex="1" gap={1}>
      **Eight general-purpose I/O lines**

      The GPIP pins can be configured as inputs or outputs. On the ST, several are connected to real hardware signals such as keyboard/MIDI activity, floppy-controller activity, and serial-interface status.
    </box>
  </row>
</box>

These are the MFP’s main hardware functions; its 24 byte-wide registers are mapped into the 68000’s memory space, starting at odd addresses around `$FFFA01`. 

#### 2. How the 16 MFP interrupt sources are used

The MFP internally assigns priorities from **15 (highest) down to 0 (lowest)**. Those are *MFP priority numbers*, not 68000 IPL levels.

| MFP priority | Atari ST use |
|---:|---|
| 15 | Monochrome-monitor detection |
| 14 | RS-232 ring indicator |
| 13 | System clock / Timer A |
| 12–9 | RS-232 receive and transmit events |
| 8 | Timer B / horizontal-line counter |
| 7 | Floppy-controller/DMA-related interrupt |
| 6 | Keyboard and MIDI ACIA interrupt |
| 5 | Timer C |
| 4 | Timer D / baud-rate-related timing |
| 3 | Generally unused in the standard mapping |
| 2–0 | RS-232 status and Centronics busy signals |

Exact usage varies with TOS version, hardware model, and software configuration; not every source is necessarily enabled or handled through an interrupt. 

##### Example: receiving a keyboard key

1. The keyboard sends a scan code to the ST’s keyboard ACIA.
2. The ACIA signals that it needs service.
3. The MFP raises its interrupt output to the 68000 at **level 6**.
4. The 68000 accepts the interrupt if its interrupt mask permits level 6.
5. During interrupt acknowledge, the MFP supplies a vector identifying its selected source.
6. The handler reads the relevant ACIA status/data and processes the received byte.

MIDI uses another ACIA, but shares the MFP’s level-6 CPU interrupt path. Software distinguishes the source by examining the ACIA status. This is a good example of why **CPU interrupt level and device interrupt source are two different layers**. 

#### 3. How the 68000 IPL pins interact with video hardware

The important chip here is **GLUE**, Atari’s logic chip that coordinates video timing, interrupt priority, bus control and other system signals.

The video hardware does not directly compete with the MFP by driving the same pins. Instead, GLUE combines the interrupt requests and encodes the highest-priority active request onto the 68000’s IPL inputs. 

<box border radius="lg" padding={3} gap={2} align="center">
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center" gap={1}>
    **Video timing**
    <text color="secondary" size="sm">Horizontal blank (HBL) · Vertical blank (VBL)</text>
  </box>
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center" gap={1}>
    **MFP 68901**
    <text color="secondary" size="sm">Timer and peripheral interrupt requests</text>
  </box>
  <icon name="arrow-down-up" size="xl" color="secondary" />
  <box background="rgba(59,130,246,0.12)" radius="md" padding={3} width="100%" align="center" gap={1}>
    **GLUE**
    <text size="sm">Combines requests and encodes priority</text>
  </box>
  <icon name="arrow-down" size="xl" color="secondary" />
  <box border radius="md" padding={3} width="100%" align="center" gap={1}>
    **68000 IPL inputs**
    <text size="sm" color="secondary">CPU sees one encoded interrupt level</text>
  </box>
</box>

On the original ST, the normal assignments are:

| CPU interrupt level | Source | Typical purpose |
|---:|---|---|
| 6 | MFP | Keyboard/MIDI, timers, serial and peripheral events |
| 4 | Video vertical blank | Once per frame; TOS housekeeping and VBL routines |
| 2 | Video horizontal blank | Line timing and specialized raster effects |

Levels 1, 3 and 5 are not available as normal interrupt levels in the standard ST configuration. The hardware effectively uses the available IPL inputs to provide the even-numbered levels 2, 4 and 6. 

##### Are there conflicts when interrupts happen together?

Not an electrical conflict. The hardware encodes the priority; the CPU then applies its own interrupt mask.

For example, suppose a level-2 HBL request occurs while the MFP is requesting level 6:

- If the CPU can accept level 6, it services the MFP interrupt first.
- A level-2 interrupt cannot interrupt a level-6 handler.
- Once the CPU returns from the level-6 handler, a still-pending lower-priority request can be serviced, subject to the interrupt mask and the request still being active.

Similarly, level 4 has priority over level 2. The CPU’s interrupt mask in the status register controls which levels it accepts; it does not replace the MFP’s separate per-source interrupt enable and mask registers.

**One subtle distinction:** the video-generated HBL interrupt at CPU level 2 is not the same thing as the MFP’s Timer B interrupt. Timer B can count line-related events and generate its own interrupt, but that interrupt arrives through the MFP at CPU level 6. That distinction matters when programming raster effects. 

#### 4. Are NMI interrupts used on the Atari ST?

**On the original Atari ST, there is no normal hardware NMI source in the standard configuration.** Although the 68000 supports level 7 and has an NMI exception vector, that does not mean the ST routinely generates level-7 interrupts.

At level 7, the 68000 recognizes the special interrupt condition when all three IPL inputs indicate active-low. But the original ST’s interrupt wiring uses only the inputs needed for levels 2, 4 and 6; level 7 is therefore not a normal interrupt path available to software. Atari’s hardware references list level 7 as NMI in the processor’s general interrupt table, while the ST-specific hardware description explains that the normal system uses only two IPL inputs. 

It is useful to separate three things:

- **NMI capability:** a feature of the 68000 processor.
- **NMI vector:** an entry in the CPU’s exception-vector table.
- **An actual NMI source:** hardware that asserts the level-7 condition.

The first two exist; the third is not normally provided by the original ST’s interrupt circuitry. Reset is also a separate hardware mechanism, not an ordinary level-7 interrupt.

Later Atari models have different interrupt arrangements, so this description is specifically about the original ST family’s standard configuration.

#### 5. Practical takeaway for programming

When writing an interrupt handler for the ST, think in three layers:

1. **Source:** Which device or event happened? For example, the keyboard ACIA, Timer A, VBL or HBL.
2. **Routing and priority:** Does it enter through the MFP at level 6, or through GLUE’s video interrupt path at level 4 or 2?
3. **CPU acceptance:** Does the 68000’s SR interrupt mask allow that level to run now?

For normal peripheral work, you configure the MFP’s source-specific registers and vector. For frame-synchronized work, VBL is generally the convenient place for once-per-frame tasks; HBL and Timer B are useful for more precise line-based effects, but require careful timing.

The main rule is: **the MFP chooses among its own sources; GLUE combines the major interrupt paths; the 68000 decides when an interrupt can be accepted.**

---

## Turn 07 — 2026-10-27 22:48 UTC

### Question

> explain the function codes of the 68000, the bus control signals and the DMA principle, and how the buses access is managed overall in the atari st

### Answer

The Atari ST’s bus system makes much more sense if you separate three questions:

1. **What kind of access is the 68000 performing?** — the *function codes* answer this.
2. **How does a read or write actually happen?** — the bus-control signals coordinate it.
3. **How can another device use memory without the CPU copying every byte?** — that is the purpose of DMA, with bus arbitration deciding who controls the bus.

The original ST combines the 68000, RAM, ROM, GLUE, the video Shifter, and a DMA controller around shared memory and bus resources. The important consequence is that the CPU is not the only device that needs access to memory. 

#### 1. The 68000 function codes (FC2–FC0)

The 68000 has three output pins, `FC2`, `FC1` and `FC0`. They identify the *type of bus cycle*, not the address being accessed.

For the original 68000, the main function-code combinations are:

| FC2–FC0 | Meaning |
|---|---|
| `001` | User data |
| `010` | User program |
| `011` | User CPU space |
| `101` | Supervisor data |
| `110` | Supervisor program |
| `111` | Supervisor CPU space |

`000` and `100` are not normal memory-access function codes.

##### Why distinguish program and data?

The 68000 has two privilege modes: **user** and **supervisor**. Supervisor mode is used by the operating system and exception handlers; user mode is used for ordinary applications.

Within each mode, the function code distinguishes instruction fetches from data accesses.

For example, if a program executes:

```asm
MOVE.W  $20000,D0
```

the CPU performs a data read. If it fetches the next instruction from memory, that is a program access. The address might be in the same RAM chip, but the function-code pins identify different types of access.

This lets external hardware distinguish accesses based on privilege or purpose. In a system with suitable protection hardware, it can be used to prevent user-mode software from accessing supervisor-only resources. The original Atari ST does **not** provide full memory protection simply because these pins exist. 

##### CPU space and interrupt acknowledge

`111` is especially interesting. The 68000 uses supervisor CPU-space cycles for special operations such as interrupt acknowledge.

During an interrupt-acknowledge cycle, the CPU is not simply reading an ordinary memory address. It is asking the interrupting hardware to provide a vector number—or signalling that an autovector should be used.

That is why the function codes are useful beyond ordinary program execution: external hardware can tell an interrupt-acknowledge cycle apart from a normal RAM read.

#### 2. The bus-control signals

The 68000 has a 24-bit address bus, a 16-bit data bus, and several control signals that indicate when addresses and data are valid and whether the CPU is reading or writing. The original 68000 can address up to 16 MiB of byte-addressable space. 

<box border radius="lg" padding={3} gap={3}>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" justify="center" size="44px">
      <icon name="map-pin" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **A1–A23 — address bus**

      Specifies the memory address or peripheral address being accessed. The original 68000 does not output A0 as a separate address pin; byte selection is handled using UDS and LDS.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" justify="center" size="44px">
      <icon name="arrow-left-right" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **D0–D15 — data bus**

      Carries data both into and out of the CPU. It can transfer a 16-bit word or one byte at a time.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" justify="center" size="44px">
      <icon name="activity" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **AS — Address Strobe**

      Active low. Indicates that the address on the bus is valid for the current transfer.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" justify="center" size="44px">
      <icon name="arrow-down-up" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **R/W — Read/Write**

      High means a read; low means a write.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" justify="center" size="44px">
      <icon name="columns-2" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **UDS and LDS — byte-lane selection**

      Select the upper byte (`D15–D8`) and lower byte (`D7–D0`) respectively. Both are asserted for a word transfer; only one is asserted for a byte transfer.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} align="center" justify="center" size="44px">
      <icon name="check-circle" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **DTACK — Data Transfer Acknowledge**

      Input to the CPU. The selected device asserts it when the transfer can complete. If the response is delayed, the CPU inserts wait states rather than assuming the data is ready.
    </box>
  </row>
</box>

The control signals are active low where indicated by the conventional slash or overbar notation, such as `/AS`, `/UDS`, `/LDS` and `/DTACK`. Saying a signal is “asserted” means it is active, whether that corresponds to a low or high voltage. 

##### How a memory read works

Imagine the CPU reading a word from RAM.

<list connector="solid" marker="number" gap={2}>
  <list-item>**Address:** the CPU places the address on A1–A23 and sets the function codes and R/W signal.</list-item>
  <list-item>**Select:** it asserts AS and the relevant data strobe(s), telling the memory interface which transfer is taking place.</list-item>
  <list-item>**Wait:** the memory interface decodes the address and returns data. If the transfer is not ready, DTACK remains inactive and the CPU waits.</list-item>
  <list-item>**Complete:** DTACK is asserted, the CPU accepts the data, and the bus cycle terminates.</list-item>
</list>

A write is similar, except the CPU drives the data bus and sets R/W low. This is an asynchronous handshake: the device can determine when it is ready instead of being forced to respond after a fixed number of CPU clocks. 

#### 3. DMA: Direct Memory Access

DMA lets a device transfer data to or from RAM without the CPU executing an instruction for every byte or word.

Without DMA, reading a disk sector could involve the CPU repeatedly reading the disk controller and storing each byte into RAM. With DMA, the CPU configures the transfer and the hardware moves the data directly.

<box border radius="lg" padding={3} gap={2} align="center">
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **CPU**
    <text color="secondary" size="sm">Sets the RAM address, transfer direction and command</text>
  </box>
  <icon name="arrow-down" size="lg" />
  <box background="rgba(59,130,246,0.12)" radius="md" padding={3} width="100%" align="center">
    **DMA controller + memory controller**
    <text color="secondary" size="sm">Arbitrate for the bus and coordinate the transfer</text>
  </box>
  <icon name="arrow-down-up" size="lg" />
  <row gap={2} width="100%">
    <box border radius="md" padding={3} flex="1" align="center">
      **Disk controller**
      <text color="secondary" size="sm">Supplies or receives data</text>
    </box>
    <box border radius="md" padding={3} flex="1" align="center">
      **RAM**
      <text color="secondary" size="sm">Stores or supplies data</text>
    </box>
  </row>
</box>

The DMA controller does not mean that the disk controller itself can independently address RAM. On the original ST, the custom DMA controller works together with the MMU/memory controller and GLUE, which provide address generation, memory access and arbitration functions. 

##### What happens during a DMA transfer?

For a floppy read, the process is approximately:

1. The operating system sets up the DMA address and commands the floppy controller.
2. When data is ready, the DMA circuitry requests access to the shared bus.
3. GLUE coordinates the request with the 68000.
4. The CPU finishes its current bus cycle and relinquishes control.
5. The DMA and memory-controller circuitry move the data between the floppy controller and RAM.
6. When the transfer finishes, the bus is released and the CPU continues normal execution.

The CPU can do other work between DMA transfers, but it cannot use the shared bus while another bus master owns it. DMA therefore improves CPU efficiency; it does not make memory bandwidth unlimited. 

#### 4. How the 68000 gives up the bus

The original 68000 provides three bus-arbitration signals:

| Signal | Direction | Purpose |
|---|---|---|
| `BR` — Bus Request | Input to CPU | Another device requests bus ownership |
| `BG` — Bus Grant | Output from CPU | CPU indicates that it will release the bus |
| `BGACK` — Bus Grant Acknowledge | Input to CPU | The other bus master confirms that it has taken over |

These signals implement a handshake. They are distinct from `DTACK`, which finishes an individual bus transfer.

The key distinction is:

- **DTACK:** “This particular read or write is complete.”
- **BR/BG/BGACK:** “Another device needs to take control of the bus.”

When the bus is handed over, the CPU’s address, data and control outputs become high-impedance, so the new bus master can drive them without electrical contention. 

#### 5. The special case: video Shifter access

Video display is another consumer of RAM bandwidth. The Shifter must fetch screen data at the right time to keep generating the display, so it cannot simply wait until the CPU has finished whatever it is doing.

On the original ST, **the MMU/memory controller coordinates CPU accesses and video fetches**. The Shifter handles the video data and pixel stream, while the memory controller supplies the necessary RAM addresses and data. Video access is scheduled into the memory system; it is not simply an ordinary CPU instruction or the same kind of floppy-DMA transfer. 

A simplified view of the original ST's memory arbitration is:

<box border radius="lg" padding={3} gap={2} align="center">
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **CPU — 68000**
    <text color="secondary" size="sm">Normal program and data accesses</text>
  </box>
  <row align="center" gap={2}>
    <icon name="arrow-down-up" size="lg" color="secondary" />
    <text color="secondary" size="sm">Shared memory access coordination</text>
  </row>
  <box background="rgba(59,130,246,0.12)" radius="md" padding={3} width="100%" align="center">
    **MMU / memory controller**
    <text color="secondary" size="sm">RAM timing, addressing and access scheduling</text>
  </box>
  <row gap={2} width="100%">
    <box border radius="md" padding={3} flex="1" align="center">
      **Video Shifter**
      <text color="secondary" size="sm">Display fetches</text>
    </box>
    <box border radius="md" padding={3} flex="1" align="center">
      **DMA controller**
      <text color="secondary" size="sm">Disk data transfers</text>
    </box>
  </row>
</box>

Video fetches are time-critical, so the memory system reserves the necessary access opportunities for display. The CPU may have to wait for an available memory slot, even though it has not explicitly surrendered the bus through a full DMA bus-grant handshake.

Disk DMA, by contrast, uses the bus-arbitration mechanism to transfer control away from the CPU. These are two related but distinct forms of resource sharing. 

#### 6. Putting the whole Atari ST bus system together

The responsibilities are divided among the main components:

| Component | Main responsibility |
|---|---|
| **68000** | Executes instructions and initiates normal bus cycles |
| **GLUE** | Decodes addresses, generates chip selects, coordinates interrupts and arbitrates bus requests |
| **MMU / memory controller** | Controls DRAM timing and addressing, refreshes RAM, and coordinates video and DMA memory access |
| **Shifter** | Converts fetched screen data into video output |
| **DMA controller** | Coordinates high-speed transfers for floppy and compatible storage devices |
| **RAM / ROM / peripherals** | Respond to the selected memory or I/O access |

The ST’s MMU is not a modern virtual-memory manager: it does not provide general virtual-address translation or paging. Its main job is managing physical memory access and timing. 

The most useful mental model is that the ST has **one shared memory system, several potential users, and hardware that schedules their access**. Function codes describe the kind of CPU cycle; bus-control signals complete individual transfers; arbitration allows another master to take the bus; and the memory controller coordinates CPU, video and DMA access to RAM.

---

## Turn 08 — 2026-10-27 23:09 UTC

### Question

> give me the detailed algorithm if how the atari st components manage the bus requests from the 68000 and all the peripherals

### Answer

### Atari ST bus arbitration: the detailed algorithm

The most important distinction is that the original Atari ST has **two related but different kinds of bus management**:

- **CPU bus arbitration:** GLUE participates in the `BR` / `BG` / `BGACK` handshake when a DMA master needs to take over the bus from the 68000.
- **RAM access scheduling:** the MMU coordinates CPU and video access to dynamic RAM, interleaving CPU memory cycles with video fetches.

These are not the same mechanism. The video Shifter does not normally take over the 68000 bus through the same full handshake used by a DMA master; video fetches are scheduled by the MMU. The DMA controller, on the other hand, can request bus mastership for disk transfers. 

#### 1. Which components can request or control access?

<box border radius="lg" padding={3} gap={3}>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="44px" align="center" justify="center">
      <icon name="cpu" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **68000 CPU**

      Initiates ordinary instruction-fetch and data-transfer cycles. It is the normal bus master when no other master has taken control.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="44px" align="center" justify="center">
      <icon name="settings-2" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **GLUE**

      Decodes addresses and chip selects, handles system timing and interrupts, and participates in bus arbitration for DMA requests.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="44px" align="center" justify="center">
      <icon name="memory-stick" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **MMU / memory controller**

      Generates DRAM control and refresh timing, manages RAM accesses, and schedules video memory fetches alongside CPU accesses.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="44px" align="center" justify="center">
      <icon name="hard-drive" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **DMA controller**

      Requests bus access for disk transfers, generates transfer addresses and coordinates data movement between a peripheral controller and RAM.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="44px" align="center" justify="center">
      <icon name="monitor" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **Video Shifter**

      Consumes screen data fetched through the MMU. Its timing requirements are handled by scheduled RAM access, rather than ordinary CPU-style bus requests.
    </box>
  </row>
</box>

The MFP, ACIA, YM2149 sound chip and ordinary I/O registers are generally **bus slaves, not bus masters**. They can request interrupts or exchange data when the CPU accesses them, but they do not independently seize the main bus to copy data into RAM.

The DMA controller is different: it can become a bus master. The original ST also has an expansion bus through which compatible external hardware can request bus ownership. 

#### 2. Algorithm A — ordinary 68000 memory access

This is the normal case when the CPU executes an instruction that reads or writes RAM.

<CodeBlock language="mermaid" model_previewable>
flowchart TD
    A["CPU needs memory access"] --> B["Place address, FC, R/W on bus"]
    B --> C["Assert AS and byte strobe(s)"]
    C --> D{"RAM access slot available?"}
    D -- No --> E["MMU delays completion"]
    E --> D
    D -- Yes --> F["MMU performs DRAM access"]
    F --> G["Data valid; DTACK asserted"]
    G --> H["CPU completes bus cycle"]
    H --> I["Release bus-cycle signals"]
</CodeBlock>

Step by step:

1. The CPU starts a bus cycle and presents the address, function codes and read/write direction.
2. GLUE decodes the address and generates the appropriate selection signals for the target device.
3. If the target is RAM, the MMU manages the DRAM address multiplexing, row/column timing and read/write control.
4. If the RAM access must wait for a video fetch or another required memory operation, the MMU delays completion.
5. Once the transfer can complete, the memory interface returns the data and asserts `DTACK`.
6. The CPU completes that bus cycle and continues executing.

**Important:** the CPU does not necessarily finish the entire instruction before the bus is available for another activity. Bus arbitration operates at bus-cycle boundaries, not just at instruction boundaries. 

#### 3. Algorithm B — video fetches and CPU memory slots

The Shifter must receive screen data in time to generate the next pixels. The MMU therefore schedules RAM access for video as well as the CPU.

The original ST’s memory timing is closely tied to its clock system. In the usual model, the MMU alternates CPU and video RAM access opportunities. A CPU access can be delayed when its required timing does not line up with an available slot. 

<box border radius="lg" padding={3} gap={2}>
  <text color="secondary" size="sm" weight="medium">Conceptual RAM-slot schedule — not to scale</text>
  <grid columns={4} gap={1}>
    {#each [{name:"CPU",desc:"Read/write"},{name:"VIDEO",desc:"Shifter fetch"},{name:"CPU",desc:"Read/write"},{name:"VIDEO",desc:"Shifter fetch"}] as slot}
      <grid-item>
        <box background={slot.name==="VIDEO"?"rgba(59,130,246,0.14)":"surface-secondary"} radius="md" padding={2} align="center" gap={1}>
          <text weight="medium">{slot.name}</text>
          <text size="xs" color="secondary" textAlign="center">{slot.desc}</text>
        </box>
      </grid-item>
    {/each}
  </grid>
  <text color="secondary" size="xs">Simplified illustration of alternating access opportunities; actual timing depends on video mode and display phase.</text>
</box>

The sequence is:

1. The MMU knows the video-memory address needed next.
2. It schedules the corresponding DRAM fetch.
3. The Shifter receives the fetched word and shifts out the pixel data.
4. When the memory schedule permits, CPU accesses proceed.
5. This repeats throughout the display.

This is not the same as a separate peripheral asking to become bus master. The video path has its own timing requirements, and the MMU meets them as part of normal memory operation.

#### 4. Algorithm C — DMA requests bus mastership

This is the most important handshake to understand. The original ST’s DMA circuitry requests access through the system logic, and GLUE participates in the 68000’s bus-arbitration protocol. 

The bus-control signals are active low, so `/BR`, `/BG` and `/BGACK` are asserted by going low.

<CodeBlock language="mermaid" model_previewable>
sequenceDiagram
    participant DMA as DMA controller
    participant G as GLUE / arbitration
    participant CPU as 68000
    participant MMU as MMU / RAM

    DMA->>G: Request bus access
    G->>CPU: Assert /BR
    Note over CPU: Finish current bus cycle
    CPU-->>G: Assert /BG
    G->>DMA: Grant is available
    Note over DMA: Wait for CPU bus cycle signals to clear
    DMA->>CPU: Assert /BGACK
    Note over CPU: CPU bus outputs become high impedance
    DMA->>MMU: Perform DMA memory cycles
    MMU-->>DMA: Complete transfers
    DMA-->>CPU: Release /BGACK
    Note over CPU: Resume bus ownership
</CodeBlock>

##### The full sequence

**1. A transfer is prepared.** The operating system programs the DMA address, transfer mode and other relevant registers, then commands the peripheral controller.

**2. The DMA controller requests the bus.** Its request reaches the system arbitration logic. GLUE asserts the 68000’s `/BR` input.

**3. The CPU finishes its current bus cycle.** It does not abandon an in-progress read or write halfway through. Once it can relinquish control, it asserts `/BG`.

**4. The DMA side takes control.** It waits for the relevant CPU bus-cycle signals to become inactive, then asserts `/BGACK` to indicate that it has taken over.

**5. The CPU releases its bus outputs.** The address, data and control signals become high-impedance, avoiding contention between the CPU and DMA circuitry.

**6. DMA transfers take place.** The DMA circuitry supplies the appropriate addresses and coordinates the data transfers. The MMU still manages physical RAM timing; bus ownership does not bypass DRAM requirements.

**7. DMA releases the bus.** When the bus-master operation ends, `/BGACK` is deasserted and normal CPU bus ownership resumes.

A subtle point: bus ownership can change between bus cycles during an instruction. The CPU may therefore be temporarily stalled in the middle of executing an instruction, then continue after the DMA operation. 

#### 5. Algorithm D — what happens when several things need attention?

Not every peripheral request is a bus request. This distinction avoids a common misconception.

| Event | What it requests | How it is handled |
|---|---|---|
| CPU reads RAM | A memory transfer | MMU schedules and completes the access |
| Shifter needs screen data | A timed video-memory fetch | MMU schedules the fetch |
| Floppy DMA needs RAM | Bus mastership and memory transfers | GLUE arbitration plus DMA/MMU coordination |
| MFP timer expires | Interrupt service | MFP raises an interrupt request |
| Keyboard ACIA receives data | Interrupt service | ACIA requests service through the MFP |
| YM2149 needs a register update | A register access | CPU writes its memory-mapped I/O register |

Interrupt requests do not automatically take over the bus. An interrupt tells the CPU that software needs to service an event; the CPU then performs the required exception-entry bus cycles and runs the handler when its interrupt mask permits.

For example, a keyboard interrupt can occur while disk DMA is active. The interrupt request can become pending, but the CPU cannot run the interrupt handler while it has relinquished bus ownership. It must regain control before it can execute the handler's instructions.

#### 6. The complete system algorithm

This is a simplified view of the repeated scheduling decisions in an original Atari ST.

<CodeBlock language="mermaid" model_previewable>
flowchart TD
    A["System running"] --> B{"Video fetch due?"}
    B -- Yes --> C["MMU services video RAM access"]
    C --> A
    B -- No --> D{"DMA needs bus?"}
    D -- Yes --> E["GLUE requests CPU bus release"]
    E --> F["CPU finishes bus cycle and grants bus"]
    F --> G["DMA performs transfer cycles"]
    G --> H["DMA releases bus"]
    H --> A
    D -- No --> I{"CPU bus cycle pending?"}
    I -- Yes --> J["MMU services CPU memory access"]
    J --> A
    I -- No --> K["CPU performs internal work"]
    K --> A
</CodeBlock>

This diagram is a conceptual algorithm, not a literal listing of GLUE or MMU internal logic. In particular, video scheduling and CPU timing are tightly coupled, so they are not always independent requests checked in a simple sequential priority loop.

##### The central rule

The original ST uses **timing-based RAM scheduling for CPU and video access, plus bus-master arbitration for DMA**. GLUE coordinates the system-level control, the MMU handles physical RAM timing and video access, and the DMA controller carries out bulk transfers.

That separation explains why a CPU memory access can be delayed by video even without DMA, and why disk DMA can temporarily prevent the CPU from executing even though the CPU has not stopped generating interrupts or other internal activity.

---

## Turn 09 — 2026-10-27 23:25 UTC

### Question

> how can a video memory read take place for the shifter if the cpu owns the bus and is in the middle of a read. what does the mmu "timing based ram scheduling mean exactly ?

### Answer

Your question gets to the heart of the Atari ST design: **the CPU can appear to own the bus for a whole read cycle, yet the Shifter can access RAM during that same cycle because the MMU schedules the two accesses into different time slots.** The CPU’s bus signals remain active, but the RAM itself is controlled by the MMU, which can serve the video fetch during a phase when the CPU does not need the RAM data bus.

The clearest hardware description I found is the MAME Atari ST video implementation: it describes the MMU as alternating CPU and video accesses, each taking eight cycles of the approximately 32 MHz master clock. 

#### 1. What “timing-based RAM scheduling” actually means

The original ST has an 8 MHz 68000, but its memory controller operates from a faster clock. A typical CPU bus cycle takes about 500 ns, while a half-cycle is about 250 ns.

The MMU uses this difference to fit a video fetch into a CPU bus cycle.

<box border radius="lg" padding={3} gap={3}>
  <title size="lg">One 500 ns CPU bus cycle</title>
  <text color="secondary" size="sm">Conceptual timing for a typical RAM read</text>
  <box gap={1}>
    <row gap={1}>
      <box background="rgba(59,130,246,0.18)" radius="md" padding={2} flex="1" align="center" gap={1}>
        <text weight="medium">First 250 ns</text>
        <text size="sm" textAlign="center">Video RAM fetch</text>
      </box>
      <box background="rgba(22,163,74,0.16)" radius="md" padding={2} flex="1" align="center" gap={1}>
        <text weight="medium">Second 250 ns</text>
        <text size="sm" textAlign="center">CPU RAM access</text>
      </box>
    </row>
    <row justify="between">
      <text color="secondary" size="xs">0 ns</text>
      <text color="secondary" size="xs">250 ns</text>
      <text color="secondary" size="xs">500 ns</text>
    </row>
  </box>
  <text color="secondary" size="sm">Simplified timing model. The precise phase and access timing depend on the bus cycle, video mode and clock alignment.</text>
</box>

The CPU and Shifter are not both driving the DRAM data bus at once. Instead, the MMU controls the DRAM signals and arranges for the memory to serve one access, then the other.

This is possible because the CPU does not need the RAM data during every part of its bus cycle. During part of a read cycle, it is establishing the address and performing bus handshaking; its data sampling occurs later. The MMU exploits that timing window for the video fetch. 

#### 2. What happens electrically?

Consider a CPU read from RAM at address `$000400`.

1. The 68000 places its address on the CPU address bus and asserts the appropriate bus-control signals.
2. The MMU recognizes a CPU memory access and controls the DRAM address multiplexing and control signals.
3. During the scheduled video phase, the MMU selects the address from its video-address counter and fetches the next screen word.
4. The Shifter receives that word through the video data path and stores it for later pixel output.
5. During the CPU phase, the MMU selects the CPU's address and performs the requested read.
6. The CPU receives the data when the cycle completes.

The critical detail is step 3: **the video address does not have to come from the CPU's address bus.** The MMU has its own video-address counter, so it can generate the memory address for the Shifter independently. It also has a separate counter used for DMA addressing. 

That is why it is misleading to imagine the Shifter waiting for the CPU to finish every instruction before it can read the screen.

#### 3. But isn't the CPU still asserting AS?

Yes. This is the subtle part.

The CPU can keep its address and bus-cycle control signals asserted while the MMU temporarily uses the DRAM interface for the video fetch. The CPU address is still present on the CPU-side bus, but the MMU controls which address reaches the DRAM chips and when the RAM data is used.

There are therefore two related but distinct interfaces:

- **CPU bus:** the 68000's address, data and control signals.
- **DRAM interface:** the multiplexed row/column addresses and RAS/CAS control signals driven by the MMU.

The MMU sits between these two worlds. It can schedule the DRAM operation without requiring the CPU to perform a separate bus-arbitration handshake for each video fetch. 

#### 4. Does the CPU ever have to wait?

Yes, in some timing situations. The ST is not a system in which every possible CPU access always completes in exactly the same time.

The MMU normally interleaves CPU and video access opportunities. But if a CPU access starts at a phase that conflicts with the memory schedule, the MMU can delay completion using the CPU's wait-state mechanism. The 68000 waits for the cycle to finish rather than sampling invalid data.

This is particularly important when analyzing exact cycle timing or writing raster effects. The relevant timing is not merely “the CPU is 8 MHz”; it is the relationship among the CPU bus cycle, the MMU's clock phases, the video-fetch schedule and the Shifter's data pipeline. 

#### 5. Why disk DMA is different

Video fetching is a scheduled memory operation controlled by the MMU. Disk DMA is a bus-master transfer that needs bus ownership.

| Video fetch | Disk DMA |
|---|---|
| MMU generates the video address | DMA controller supplies transfer addresses |
| Scheduled into the memory timing | Requests bus ownership through arbitration |
| No full CPU bus-grant handshake for each fetch | Uses the bus-arbitration protocol |
| Shifter consumes the fetched data | Data moves between RAM and the peripheral controller |

The two mechanisms coexist, but they should not be conflated. 

**In one sentence:** the MMU doesn't make two devices use RAM simultaneously; it makes their accesses happen at different moments within a coordinated memory-timing schedule, using its own address-generation and DRAM-control circuitry to keep the video stream supplied while the CPU continues executing.

---

## Turn 10 — 2026-10-27 23:34 UTC

### Question

> tell me more about the mmu / ram interface signals, adddress / data path, and timing and refresh requirements

### Answer

### Atari ST MMU: RAM interface, address/data paths, timing and refresh

The Atari ST’s MMU is a particularly interesting chip because it is not just a conventional memory-address decoder. It sits between the 68000 and dynamic RAM, generates the signals needed to operate the DRAM chips, schedules video fetches for the Shifter, and participates in DMA memory transfers.

A useful starting point is the MMU pinout and the detailed description in *Atari ST Internals*. The MMU exposes multiplexed memory-address lines `MAD0–MAD9`, bank-selection strobes, byte-lane strobes, write control, data connections and timing signals. 

#### 1. The main signal groups

<box border radius="lg" padding={3} gap={3}>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="44px" align="center" justify="center">
      <icon name="map" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **CPU-side address and control**

      `A1–A23`, `AS`, `R/W`, `UDS`, `LDS` and the associated bus-cycle signals tell the MMU which address is being accessed, whether it is a read or write, and which byte lane is involved.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="44px" align="center" justify="center">
      <icon name="git-merge" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **Multiplexed DRAM address**

      `MAD0–MAD9` carry row and column address values at different times. This reduces the number of physical address pins required on the RAM chips.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="44px" align="center" justify="center">
      <icon name="layers" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **DRAM control and bank selection**

      `RAS0`, `RAS1`, `CAS0L`, `CAS0H`, `CAS1L`, `CAS1H` and `WE` select a memory bank, select its byte lane, and control row/column access and writing.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="44px" align="center" justify="center">
      <icon name="arrow-left-right" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **Data and transfer timing**

      The MMU connects the memory data path to the CPU bus and handles memory-cycle timing, including CPU completion through `DTACK`. Other MMU signals coordinate video data and DMA-related transfers.
    </box>
  </row>
</box>

The exact pin names and polarity matter: many of these signals are active-low. Also, this describes the original ST-family discrete MMU; later machines such as the STE integrate or revise some of these functions. 

#### 2. How the address path works

The ST uses dynamic RAM (DRAM), which is organized as a matrix of rows and columns. Rather than providing separate pins for the full row and column addresses, the RAM receives them sequentially on the same address pins.

That is the purpose of the MMU’s `MAD0–MAD9` signals: they carry the multiplexed DRAM address. 

<box border radius="lg" padding={3} gap={2} align="center">
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **68000 address bus**
    <text color="secondary" size="sm">A1–A23 · CPU's requested address</text>
  </box>
  <icon name="arrow-down" size="lg" />
  <box background="rgba(59,130,246,0.12)" radius="md" padding={3} width="100%" align="center">
    **MMU**
    <text color="secondary" size="sm">Selects bank and derives row/column addresses</text>
  </box>
  <icon name="arrow-down" size="lg" />
  <box border radius="md" padding={3} width="100%" align="center">
    **MAD0–MAD9**
    <text color="secondary" size="sm">Row address first, column address next</text>
  </box>
  <icon name="arrow-down" size="lg" />
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **DRAM chips**
    <text color="secondary" size="sm">Select the cell at the chosen row and column</text>
  </box>
</box>

##### Why multiplex the address?

Imagine a DRAM chip containing a matrix with 512 rows and 512 columns. In principle, you need nine bits to select a row and another nine bits to select a column. The same nine address pins can carry both values at different times, instead of requiring eighteen separate address pins.

The ST's MMU has ten multiplexed address outputs, `MAD0–MAD9`, supporting the row/column organization of the DRAM configurations it was designed for.

This is not virtual-memory translation: the MMU is transforming and sequencing a physical RAM address, not mapping a process's virtual address onto arbitrary physical pages.

#### 3. The RAS/CAS sequence

The two principal DRAM timing signals are:

- **RAS** — Row Address Strobe.
- **CAS** — Column Address Strobe.

The conventional active-low notation is `/RAS` and `/CAS`.

A typical read sequence looks like this:

<box border radius="lg" padding={3} gap={3}>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="40px" align="center" justify="center">
      <title size="lg">1</title>
    </box>
    <box flex="1" gap={1}>
      **Row phase**

      The MMU puts the row address on `MAD0–MAD9` and asserts the appropriate bank's RAS signal. The DRAM captures the row address.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="40px" align="center" justify="center">
      <title size="lg">2</title>
    </box>
    <box flex="1" gap={1}>
      **Column phase**

      The MMU changes `MAD0–MAD9` to the column address and asserts the relevant CAS signal or signals. The DRAM selects the requested column within the open row.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="40px" align="center" justify="center">
      <title size="lg">3</title>
    </box>
    <box flex="1" gap={1}>
      **Data phase**

      The selected DRAM chips drive their data outputs for a read, or capture the incoming data for a write.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="40px" align="center" justify="center">
      <title size="lg">4</title>
    </box>
    <box flex="1" gap={1}>
      **Close the cycle**

      The MMU releases the strobes as required and coordinates the completion of the transfer, including CPU `DTACK` where applicable.
    </box>
  </row>
</box>

This is a simplified sequence; actual DRAM timing includes setup and hold times, RAS-to-CAS delay, data-valid timing and recovery periods. The MMU generates the control waveforms to satisfy the memory chips' timing requirements. 

#### 4. Banks, byte lanes and the 16-bit data path

The 68000 has a 16-bit data bus. The ST's memory system therefore has to select the correct bank and the correct byte lane.

The MMU provides these principal DRAM strobes:

| Signal | Function |
|---|---|
| `RAS0` | Selects RAM bank 0's row |
| `RAS1` | Selects RAM bank 1's row |
| `CAS0L` | Selects the low byte lane in bank 0 |
| `CAS0H` | Selects the high byte lane in bank 0 |
| `CAS1L` | Selects the low byte lane in bank 1 |
| `CAS1H` | Selects the high byte lane in bank 1 |
| `WE` | Controls whether the selected DRAM is written |

These signals are active-low on the ST interface. The `L` and `H` suffixes refer to the lower and upper bytes of the 16-bit data path. 

For example, imagine a 16-bit word at an even address:

- The high byte is transferred on `D15–D8`.
- The low byte is transferred on `D7–D0`.
- Both byte lanes are selected for a word transfer.
- A byte transfer selects only the relevant lane.

The CPU's `UDS` and `LDS` signals help the MMU determine which byte lanes are required. The physical DRAM chips may each store only one bit per cell, so multiple chips work in parallel to supply the full 16-bit word.

The ST also supports different DRAM capacities and configurations. Bank size and address mapping therefore depend on the particular ST model and memory chips fitted. 

#### 5. The data path is different for CPU, video and DMA

The MMU does not always obtain a RAM address from the 68000. It has internal address-generation and control functions for the other memory users too.

<box border radius="lg" padding={3} gap={3} align="center">
  <box border radius="md" padding={3} width="100%" align="center">
    **Address sources**
    <grid columns={3} gap={2}>
      <grid-item>
        <box background="surface-secondary" radius="md" padding={2} align="center">
          <icon name="cpu" size="lg" />
          <text weight="medium">CPU</text>
          <text color="secondary" size="xs">Bus address</text>
        </box>
      </grid-item>
      <grid-item>
        <box background="surface-secondary" radius="md" padding={2} align="center">
          <icon name="monitor" size="lg" />
          <text weight="medium">Video</text>
          <text color="secondary" size="xs">Video counter</text>
        </box>
      </grid-item>
      <grid-item>
        <box background="surface-secondary" radius="md" padding={2} align="center">
          <icon name="hard-drive" size="lg" />
          <text weight="medium">DMA</text>
          <text color="secondary" size="xs">DMA address counter</text>
        </box>
      </grid-item>
    </grid>
  </box>
  <icon name="arrow-down" size="lg" />
  <box background="rgba(59,130,246,0.12)" radius="md" padding={3} width="100%" align="center">
    **MMU address and timing logic**
    <text color="secondary" size="sm">Chooses the appropriate address source and generates DRAM control signals</text>
  </box>
  <icon name="arrow-down" size="lg" />
  <box border radius="md" padding={3} width="100%" align="center">
    **DRAM array**
    <text color="secondary" size="sm">Shared physical memory</text>
  </box>
  <icon name="arrow-down-up" size="lg" />
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **Data routing**
    <text color="secondary" size="sm">CPU bus, video data path, or DMA transfer path</text>
  </box>
</box>

The video counter advances as the screen is read; the DMA address counter advances as data is transferred. The MMU's control circuitry coordinates these operations with CPU memory cycles. The Shifter receives video words through its video data path, while the CPU uses the normal 68000 data bus. 

#### 6. Refresh: why DRAM needs special treatment

Unlike SRAM, DRAM stores each bit as charge in a tiny capacitor. That charge leaks over time. To preserve the data, the system must periodically refresh the memory rows.

Refresh is a hardware responsibility, not a job for TOS or an interrupt handler.

A DRAM refresh operation activates rows in a way that restores their stored charge. The exact electrical refresh sequence depends on the DRAM chips and controller implementation. On the ST, the MMU's memory-control circuitry is responsible for providing the required timing and refresh behavior. 

A conceptual view:

<list marker="number" gap={2}>
  <list-item>**Select a row** in the DRAM array.</list-item>
  <list-item>**Activate it** using the DRAM control sequence so the stored charge can be sensed and restored.</list-item>
  <list-item>**Allow the required recovery time** before the next operation.</list-item>
  <list-item>**Repeat often enough** to meet the DRAM's data-retention and refresh specifications.</list-item>
</list>

Refresh is not the same as reading the screen. Video fetches are useful reads that deliver pixel data to the Shifter; refresh exists to preserve all the DRAM contents, including memory locations that are not being read or written by software.

It is also worth avoiding an overly simple rule such as “every odd clock cycle is refresh.” The actual MMU schedule includes CPU, video and refresh requirements, and its precise behavior depends on the implementation and timing phase.

#### 7. Clocking and access timing

The original ST derives its system timing from a master clock of approximately 32 MHz. The Shifter and MMU use derived clocks for pixel timing, memory control and CPU operation. The exact master frequency varies with the video standard and machine configuration. 

The useful relationship is:

- The 68000 runs at approximately 8 MHz.
- The MMU operates from faster timing signals to control DRAM.
- The Shifter requires video words at a fixed rate determined by the current display mode.
- The MMU schedules the RAM operations and delays CPU completion when necessary.

A conceptual memory schedule might look like this:

<box border radius="lg" padding={3} gap={2}>
  <text color="secondary" size="sm">Conceptual schedule, not a literal pin-level waveform</text>
  <grid columns={4} gap={1}>
    {#each [{label:"Video",desc:"Fetch word"},{label:"CPU",desc:"RAM access"},{label:"Video",desc:"Fetch word"},{label:"CPU",desc:"RAM access"}] as s}
      <grid-item>
        <box background={s.label==="Video"?"rgba(59,130,246,0.16)":"surface-secondary"} radius="md" padding={2} align="center" gap={1}>
          <text weight="medium">{s.label}</text>
          <text size="xs" textAlign="center" color="secondary">{s.desc}</text>
        </box>
      </grid-item>
    {/each}
  </grid>
</box>

This illustrates the interleaving principle rather than a universal waveform. Real operation depends on the CPU cycle phase, video mode, DRAM timing, refresh needs and whether DMA is active.

The MMU must ensure that the RAM sees valid row and column addresses for the required duration, that data is available at the correct point in the CPU or video transfer, and that refresh happens frequently enough. That combination of **address multiplexing, DRAM strobes, byte-lane selection, clock scheduling and refresh** is what makes the ST MMU central to the machine's design.

---

## Turn 11 — 2026-10-27 23:48 UTC

### Question

> when we talk about DMA controller, is the disk controller, the glue or the mmu the dma controller ? or is there another dedicated component ?

### Answer

**There is a separate, dedicated DMA controller chip in the original Atari ST.** It is distinct from the WD1772 disk controller, GLUE and MMU. However, the DMA system is distributed across these chips: each performs a different part of the operation. 

#### The four chips and their roles

<box gap={3}>
  <row align="start" gap={3}>
    <AsyncImage query="Atari ST DMA controller chip C025913 Atari DMA IC" aspectRatio="4:3" width="100px" />
    <box flex="1" gap={1}>
      **1. Dedicated DMA controller**

      <badge color="success">Transfer control</badge>

      Controls the disk-DMA data transfer, interfaces with the floppy controller, and handles transfer-related registers and handshaking. It does not independently generate the full RAM address; that function is shared with the MMU.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Western Digital WD1772 floppy disk controller integrated circuit DIP" aspectRatio="4:3" width="100px" />
    <box flex="1" gap={1}>
      **2. WD1772 floppy disk controller**

      <badge>Disk protocol</badge>

      Handles floppy-drive operations such as seeking tracks, reading sectors, writing sectors, and processing the raw disk data stream. It supplies or consumes the bytes; it does not perform the main RAM-addressing function.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Atari ST GLUE custom integrated circuit chip" aspectRatio="4:3" width="100px" />
    <box flex="1" gap={1}>
      **3. GLUE**

      <badge>Bus arbitration</badge>

      Coordinates bus requests and the CPU's bus-grant handshake, including `BR`, `BG` and `BGACK`. It helps the DMA subsystem obtain access to the system bus.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Atari ST MMU C025912 custom memory management chip" aspectRatio="4:3" width="100px" />
    <box flex="1" gap={1}>
      **4. MMU**

      <badge>RAM addressing</badge>

      Controls DRAM access and timing, and contains the address-counter functionality used for DMA transfers. It works with the DMA controller to access RAM.
    </box>
  </row>
</box>

The exact division of internal logic is not a simple one-chip-per-task arrangement. In particular, some DMA address registers and control functions reside in the MMU and GLUE, even though programmers treat the disk-DMA hardware as a single logical subsystem. 

#### How they work together during a floppy read

<box border radius="lg" padding={3} align="center" gap={2}>
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **WD1772**
    <text color="secondary" size="sm">Reads disk data and asserts DRQ when data is ready</text>
  </box>
  <icon name="arrow-down" size="lg" />
  <box background="rgba(59,130,246,0.12)" radius="md" padding={3} width="100%" align="center">
    **DMA controller**
    <text color="secondary" size="sm">Coordinates the byte transfer and data-path handshake</text>
  </box>
  <icon name="arrow-down-up" size="lg" />
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **GLUE + MMU**
    <text color="secondary" size="sm">GLUE coordinates bus ownership; MMU supplies RAM addressing and timing</text>
  </box>
  <icon name="arrow-down-up" size="lg" />
  <box border radius="md" padding={3} width="100%" align="center">
    **RAM**
    <text color="secondary" size="sm">Receives the sector data</text>
  </box>
</box>

In sequence:

1. The CPU programs the DMA address, transfer count and mode.
2. The CPU commands the WD1772 to read a sector.
3. The WD1772 signals that a byte is ready.
4. The DMA controller and MMU coordinate the transfer to the programmed RAM address.
5. GLUE participates in obtaining the bus when required.
6. The address advances as data is transferred; the operation ends when the requested transfer completes.

The WD1772 is therefore the **disk controller**, while the separate DMA chip is the **transfer controller**. GLUE and MMU supply essential bus-arbitration and memory-addressing functions.

One final distinction: the DMA chip's programmer-visible registers are mapped at addresses such as `$FFFF8604` and `$FFFF8606`, while the DMA address counter is exposed at `$FFFF8609`, `$FFFF860B` and `$FFFF860D`. Those registers form the software interface to the cooperating hardware.

---

## Turn 12 — 2026-10-27 23:53 UTC

### Question

> is the dma chip in the Atari st a specific chip made by atari for the machine like glue or mmu. or is it standard component from another manufacturer like the mfp

### Answer

The Atari ST’s DMA controller is **a dedicated chip designed for the Atari ST’s hardware, not a standard off-the-shelf peripheral like the 68901 MFP**. But there’s an important distinction: Atari didn’t necessarily manufacture the silicon itself. Designing a chip and physically fabricating it are different jobs.

The ST’s custom hardware was split across several cooperating chips:

| Chip | Type | Main role |
|---|---|---|
| **GLUE** | Atari-specific custom logic | Address decoding, timing and bus-control coordination |
| **MMU** | Atari-specific custom logic | DRAM control and memory addressing, including the DMA address counter |
| **DMA controller** | Dedicated ST-specific DMA chip | Coordinates disk and hard-disk DMA transfers |
| **MFP 68901** | Standard Motorola peripheral chip | Timers, serial interface, GPIO and interrupts |
| **WD1772** | Standard floppy-disk controller IC | Floppy-disk read/write operations and sector control |

The original DMA chip is commonly identified as **Atari C025913**. Later replacement or revised versions include **C100110**, with some versions manufactured by other companies, including IMP. So the manufacturer printed on a particular chip may not be Atari, even though the design is Atari-specific. 

One subtlety: the DMA controller isn’t completely self-contained. The MMU supplies its memory-address counter, while GLUE participates in the bus-arbitration handshake. It’s therefore an Atari-designed **DMA subsystem built from cooperating custom chips**, rather than a generic DMA controller you could simply drop into another computer. 

In short: **custom-designed like GLUE and MMU; not a standard Motorola peripheral like the MFP.**

---

## Turn 13 — 2026-10-27 23:55 UTC

### Question

> ok, what are its pins and its algorithm

### Answer

The Atari ST DMA controller is easiest to understand in two parts: **its external signals (pins)** and **the sequence of operations it follows to move disk data into or out of RAM**.

One important detail first: the ST’s DMA chip does not contain every part of a complete, self-contained DMA engine. Its operation depends on the MMU for the DMA memory-address counter and on GLUE for parts of bus arbitration. So we need to distinguish the DMA chip’s own pins from signals handled by the surrounding chipset.

#### 1. The DMA chip’s pins

The original ST DMA IC is a 40-pin chip, commonly marked **C025913**. A published pin description lists the main signal groups below. 

<box border radius="lg" padding={3} gap={3}>
  <title size="lg">DMA chip signal groups</title>
  <text color="secondary" size="sm">Functional overview — not a physical pin-layout diagram.</text>
  <box background="surface-secondary" radius="md" padding={3} gap={1}>
    **Data bus: D0–D15**

    <text size="sm">Sixteen bidirectional data connections to the 68000 data bus. Disk peripherals themselves transfer data in 8-bit units, so the DMA circuitry handles the conversion between the 8-bit peripheral stream and the computer’s 16-bit bus.</text>
  </box>
  <box background="surface-secondary" radius="md" padding={3} gap={1}>
    **Control and timing: R/W, CLK, RDY, ACK**

    <text size="sm">These signals control transfer direction, clocking and coordination with the memory/bus logic and peripheral interface. RDY is particularly important to the handshake with GLUE/MMU.</text>
  </box>
  <box background="surface-secondary" radius="md" padding={3} gap={1}>
    **Address and register selection: A1, FCS and disk-control signals**

    <text size="sm">A1 and chip-select/control signals participate in register access and selection of the floppy or hard-disk interface. The complete 24-bit RAM transfer address is not generated solely inside this IC.</text>
  </box>
  <box background="surface-secondary" radius="md" padding={3} gap={1}>
    **Power and ground**

    <text size="sm">The chip also needs its clock and supply connections, including +5 V and ground.</text>
  </box>
</box>

A caution about pin numbers: the signal-group summary above is not enough to wire or probe the IC safely. For an exact pin-by-pin map, use the pinout for the specific chip/package revision and verify it against the board schematic. The ST’s DMA logic spans several chips, so some signals often discussed as “DMA signals” physically belong to GLUE or the MMU instead.

#### 2. The DMA transfer algorithm

Imagine reading one sector from a floppy disk into RAM. The CPU sets up the operation, then the hardware moves the bytes without requiring the CPU to copy each one itself.

<box border radius="lg" padding={3} align="center" gap={1}>
  <box background="surface" border radius="md" padding={3} width="100%" align="center">
    **68000 CPU**

    <text size="sm">Programs the destination, transfer mode and disk operation</text>
  </box>
  <icon name="arrow-down" size="lg" color="secondary" />
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **DMA chip + WD1772**

    <text size="sm">Disk controller supplies or accepts each byte; DMA coordinates the data transfer</text>
  </box>
  <icon name="arrow-down-up" size="lg" color="secondary" />
  <box background="surface" border radius="md" padding={3} width="100%" align="center">
    **GLUE + MMU + RAM**

    <text size="sm">Bus access is coordinated, the RAM address is generated, and the data is written or read</text>
  </box>
</box>

#### 3. Step-by-step: floppy read

Here is the algorithm at the hardware level.

<list marker="number" gap={2}>
  <list-item>**CPU configures the transfer.** It resets/initializes the DMA logic, sets the RAM destination address, chooses the direction (peripheral → RAM), and loads the transfer count.</list-item>
  <list-item>**CPU commands the WD1772.** It selects the floppy controller’s command register and issues a read-sector or read-track command through the DMA chip’s peripheral interface.</list-item>
  <list-item>**WD1772 signals that a byte is ready.** Its `FDRQ` (floppy data request) signal tells the DMA that another byte is available.</list-item>
  <list-item>**DMA collects bytes in its FIFO buffers.** The documented design uses two 16-byte FIFOs, allowing one buffer to fill while the other is being transferred to RAM.</list-item>
  <list-item>**DMA requests the system bus.** GLUE coordinates the 68000 bus-grant handshake. The MMU handles the RAM access and provides the DMA address-counter functionality.</list-item>
  <list-item>**A block is transferred to RAM.** In the described FIFO cycle, 16 bytes are written as eight 16-bit words. The address advances by 16 bytes, and the DMA releases the bus when that burst is complete.</list-item>
  <list-item>**The process repeats.** The DMA keeps responding to peripheral requests and transferring buffered data until the programmed transfer count is exhausted or the operation ends. The CPU or operating system then checks completion and device status.</list-item>
</list>

This sequence is documented in the Atari ST floppy/hard-disk programming reference. 

#### 4. What the CPU actually programs

The important registers are mapped at these addresses:

| Address | Purpose |
|---|---|
| `$FF8604` | FDC data/register access or DMA sector-count access, depending on the mode |
| `$FF8606` | DMA mode control when written; DMA status when read |
| `$FF8609` | DMA address high byte |
| `$FF860B` | DMA address middle byte |
| `$FF860D` | DMA address low byte |

The address bytes must be programmed in **high → middle → low** order? No—the documented programming guidance says to write the counter in **low → middle → high** order when setting the address. Follow the ST-specific register programming rules rather than assuming a conventional CPU register layout. 

The DMA sector count is measured in 512-byte blocks, not individual bytes. For example, a count of 1 represents one 512-byte block; a count of 2 represents 1024 bytes.

#### 5. The key distinction

The DMA chip is not simply a circuit that increments an address and copies a byte. It is a **buffered transfer engine** coordinating two different rates:

- The disk controller supplies or consumes bytes at the peripheral's rate.
- The 68000 system bus and RAM operate on their own timing.
- The FIFO buffers bridge those rates.
- GLUE and MMU coordinate access to shared RAM without corrupting CPU or video accesses.

That is why the Atari ST DMA is best understood as a small, multi-chip transfer subsystem rather than an isolated chip.

---

## Turn 14 — 2026-10-28 00:03 UTC

### Question

> give me the details of the glue puns and algorithm

### Answer

The Atari ST’s **GLUE chip** is essentially the system’s hardware coordinator. It combines address decoding, video timing, interrupt coordination and parts of the 68000 bus-arbitration logic. Unlike the 68000, it does not execute instructions; it reacts to address, timing and control signals and generates the signals needed to make the rest of the machine work together.

I’ll separate its **pins and signals** from its **operating algorithm**, because GLUE works differently from a programmable controller such as the DMA chip.

#### 1. GLUE pinout — original 68-pin package

The original Atari ST GLUE is a 68-pin PLCC custom chip. The pin numbers below follow the published ST GLUE pinout. Signals marked `*` are active-low: for example, `AS*` is asserted when it is low. 

<box border radius="lg" padding={3} gap={2}>
  <title size="lg">Address and bus-control pins</title>
  | Pin(s) | Signal | Function |
  |---|---|---|
  | 2–11, 56–68 | A14–A23, A1–A13 | CPU address bus inputs |
  | 12 | AS* | Address strobe from CPU |
  | 13–15 | FC2–FC0 | CPU function codes: user/supervisor, program/data, CPU space |
  | 46–47 | LDS*, UDS* | Select lower or upper byte of the 16-bit data bus |
  | 48–49 | D0–D1 | Data-bus connections |
  | 55 | R/W* | Read/write direction |
  <text color="secondary" size="sm">The 68-pin GLUE does not connect to all 16 data bits through the two pins listed here: D0 and D1 are the data-bus pins shown in this particular pinout. Verify the complete schematic before interpreting the package as a conventional full-width bus connection.</text>
</box>

<box border radius="lg" padding={3} gap={2}>
  <title size="lg">Memory and peripheral selection</title>
  | Pin(s) | Signal | Function |
  |---|---|---|
  | 17–21 | ROM0*–ROM4* | ROM chip-select decoding |
  | 23 | RAM* | RAM-related select/control |
  | 24 | DMA* | DMA-related select |
  | 25 | DEV* | General device-select decoding |
  | 26 | FCS* | Floppy-controller/DMA-related selection |
  | 42 | 6850CS* | ACIA chip select |
  | 51 | MFPCS* | MFP chip select |
  | 53 | SNDCS* | Sound-chip select |
  <text color="secondary" size="sm">Exact names and interpretations of some select signals differ between documentation and board schematics; these are logical roles, not a guarantee that each signal directly drives only one IC.</text>
</box>

<box border radius="lg" padding={3} gap={2}>
  <title size="lg">Bus arbitration and transfer timing</title>
  | Pin | Signal | Function |
  |---:|---|---|
  | 27 | BGI* | Bus-grant input |
  | 28 | RDY | Ready/transfer coordination |
  | 29 | VPA* | Valid peripheral address / 6800-style peripheral cycle |
  | 30 | BERR* | Bus error to CPU |
  | 31 | DTACK* | Data-transfer acknowledge |
  | 40 | BR* | Bus request to 68000 |
  | 41 | BGACK* | Bus-grant acknowledge from bus master |
  | 45 | BGO* | Bus-grant output toward DMA-side logic |
  | 50 | IACK* | Interrupt-acknowledge-related control |
</box>

<box border radius="lg" padding={3} gap={2}>
  <title size="lg">Video, interrupts and clocks</title>
  | Pin | Signal | Function |
  |---:|---|---|
  | 32–33 | IPL1*, IPL2* | Interrupt-priority outputs to CPU |
  | 34 | 8 MHz | GLUE master-clock input |
  | 36 | BLANK* | Video blanking |
  | 37 | HSYNC | Horizontal synchronisation |
  | 38 | VSYNC | Vertical synchronisation |
  | 39 | DE | Display-enable signal |
  | 43 | 500 kHz | Clock output for peripheral timing |
  | 44 | MFPINT* | MFP interrupt input |
  | 54 | 2 MHz | Clock output |
  | 22 | RESET* | Reset-related signal |
  | 1, 35, 52 | VCC/GND | Power and ground |
</box>

The original ST has multiple board revisions, and some documentation labels or signal directions differ. For board repair or probing, compare the chip marking and physical pin-1 orientation with the corresponding schematic rather than relying on the functional summary alone. The service manual describes GLUE’s central roles as decoding, timing, interrupts and bus arbitration. 

#### 2. GLUE’s operating algorithm

GLUE is not a CPU and doesn't run a software program. Its algorithm is implemented as hardware logic: it continuously observes input signals and changes its outputs according to the current bus cycle and video timing.

##### A. When the 68000 accesses memory or a peripheral

<box border radius="lg" padding={3} gap={1} align="center">
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **1. CPU starts a bus cycle**

    <text size="sm">Address lines, `AS*`, `R/W*`, `UDS*`/`LDS*` and function codes become valid.</text>
  </box>
  <icon name="arrow-down" color="secondary" size="lg" />
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **2. GLUE decodes the address**

    <text size="sm">It determines whether the cycle targets ROM, a device register, DMA-related logic or another decoded region.</text>
  </box>
  <icon name="arrow-down" color="secondary" size="lg" />
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **3. GLUE and MMU coordinate the transfer**

    <text size="sm">The appropriate select and timing signals are generated; the MMU handles the DRAM access when RAM is involved.</text>
  </box>
  <icon name="arrow-down" color="secondary" size="lg" />
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **4. Transfer completes or fails**

    <text size="sm">The cycle completes through the appropriate acknowledge mechanism, or GLUE asserts a bus error when a detected fault requires it.</text>
  </box>
</box>

A useful distinction: `DTACK*` is an acknowledgement that a bus transfer can complete. `BR*`, `BG` and `BGACK*` are part of bus arbitration. They solve different problems.

##### B. When DMA needs the bus

GLUE coordinates access between the CPU and DMA subsystem so they don't drive the shared bus simultaneously.

1. The DMA subsystem signals that it needs a transfer.
2. GLUE requests the 68000 bus using `BR*`.
3. The 68000 finishes its current bus cycle and grants the bus using its bus-grant protocol.
4. GLUE coordinates the grant with the DMA side.
5. The DMA subsystem and MMU carry out the memory transfer.
6. The DMA request/grant sequence ends, and the CPU resumes normal bus use.

This is a handshake, not a fixed software loop. The exact timing depends on the current bus cycle and the hardware's readiness signals. 

##### C. When video timing advances

GLUE also generates the signals that tell the monitor and video circuitry where each scan line and frame are:

- `HSYNC` marks horizontal line synchronisation.
- `VSYNC` marks vertical frame synchronisation.
- `DE` indicates the active display region.
- `BLANK*` controls blanking outside the visible region.

As these signals change, the Shifter knows when to output visible pixels and when the display is in the border or blanking interval. The MMU supplies video data from RAM; GLUE supplies the associated display timing. 

##### D. When an interrupt occurs

GLUE combines video-related interrupt events and the MFP interrupt request into the priority signals presented to the 68000. On the standard ST, HBL corresponds to level 2, VBL to level 4 and MFP requests to level 6. The CPU's status-register mask determines which interrupt levels it accepts.

#### 3. The big picture

<box border radius="lg" padding={3} gap={2} align="center">
  <box background="surface" border radius="md" padding={3} width="100%" align="center">
    **68000 CPU**

    <text size="sm">Issues addresses and executes instructions</text>
  </box>
  <icon name="arrow-down-up" size="lg" color="secondary" />
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **GLUE**

    <text size="sm">Decodes, arbitrates, times and coordinates</text>
  </box>
  <row gap={2} width="100%">
    <box background="surface" border radius="md" padding={2} flex="1" align="center">
      **MMU**

      <text size="sm" textAlign="center">RAM and video memory timing</text>
    </box>
    <box background="surface" border radius="md" padding={2} flex="1" align="center">
      **DMA**

      <text size="sm" textAlign="center">Buffered peripheral transfers</text>
    </box>
    <box background="surface" border radius="md" padding={2} flex="1" align="center">
      **Peripherals**

      <text size="sm" textAlign="center">MFP, ACIA, sound and disk control</text>
    </box>
  </row>
</box>

**In one sentence:** the MMU controls how RAM is physically accessed, the DMA subsystem moves blocks of peripheral data, and GLUE coordinates the system's decoding, bus ownership, interrupts and video timing.

---

## Turn 15 — 2026-10-28 00:11 UTC

### Question

> what is different in the ste

### Answer

The **Atari STE (520STE/1040STE)** keeps the basic Atari ST architecture, including the 68000 CPU, but improves graphics, sound, memory capabilities and DMA. The biggest hardware change relevant to our discussion is that some of the original ST’s custom logic is integrated into a newer chip, so the GLUE/MMU/DMA arrangement is not exactly the same.

#### 1. Original ST vs. STE at a glance

<box gap={3}>
  <row align="start" gap={3}>
    <AsyncImage query="Atari 1040STF motherboard original GLUE MMU DMA chips" aspectRatio="4:3" width="44%" />
    <box flex="1" gap={1}>
      **Original ST / STF**

      - Separate GLUE, MMU, DMA and Shifter chips.
      - 512 KiB or 1 MiB RAM on common models.
      - Original ST video modes.
      - Yamaha YM2149 sound chip.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Atari 1040STE motherboard GSTMCU DMA chip" aspectRatio="4:3" width="44%" />
    <box flex="1" gap={1}>
      **STE**

      - Newer integrated **GSTMCU** combines GLUE- and MMU-related functions.
      - Separate DMA controller remains.
      - Enhanced video modes and hardware scrolling support.
      - 8-bit stereo PCM DMA sound.
    </box>
  </row>
</box>

The STE is therefore an evolution of the ST, not a completely different computer architecture. The 68000 and much of the programmer-visible system remain familiar.

#### 2. The biggest change: GLUE + MMU become GSTMCU

<box gap={2}>
  <box border radius="lg" padding={3} gap={2}>
    <title size="md">Original ST</title>
    <box background="surface-secondary" radius="md" padding={3} align="center">
      **GLUE**

      <text size="sm">Bus control, address decoding, interrupts, video timing</text>
    </box>
    <box background="surface-secondary" radius="md" padding={3} align="center">
      **MMU**

      <text size="sm">DRAM control, memory addressing, video and DMA address counters</text>
    </box>
  </box>
  <box align="center">
    <icon name="arrow-down" size="xl" />
    <text color="secondary" size="sm">Combined in the STE</text>
  </box>
  <box border radius="lg" padding={3} gap={2}>
    <box background="surface" border radius="md" padding={3} align="center">
      <title size="lg">GSTMCU</title>
      <text size="sm" textAlign="center">One chip combining the GLUE and MMU functions, with changes to support STE features</text>
    </box>
  </box>
</box>

The combined chip is called the **GSTMCU**. This reduces the number of separate custom chips and synchronizes functions that were previously split between GLUE and MMU. For example, the original ST could have phase differences between the two chips; the STE's integrated design avoids that particular issue. 

#### 3. What else changes?

| Feature | Original ST/STF | STE |
|---|---|---|
| CPU | 8 MHz 68000 | 8 MHz 68000 |
| GLUE/MMU | Separate chips | Combined GSTMCU |
| Main DMA controller | Dedicated chip | Dedicated chip remains |
| Shifter | Original video chip | Enhanced Shifter |
| Colour palette | 512 possible colours | 4,096 possible colours |
| Fine scrolling | Limited; often software-assisted | Hardware horizontal and vertical scrolling |
| Sound | YM2149 PSG | YM2149 plus stereo 8-bit DMA audio |
| Blitter | Optional on some models | Standard feature |
| RAM expansion | Often soldered DRAM | SIMM sockets on standard STE boards |

These are the major differences of the standard STE models. 

#### 4. Does the DMA algorithm change?

The basic disk-transfer algorithm is still familiar:

1. The CPU programs the DMA controller.
2. The WD1772 handles the floppy protocol.
3. The DMA subsystem coordinates data transfer.
4. The memory controller provides the RAM access and address progression.
5. The CPU checks completion and status.

The difference is that the **GSTMCU now performs the combined GLUE/MMU coordination**. The dedicated disk DMA chip is still present; it hasn't simply been absorbed into the GSTMCU. 

The STE also adds a separate use for DMA: **playing digital audio**. The enhanced Shifter and GSTMCU participate in reading audio samples from memory at a programmed rate, while the audio circuitry converts those samples into stereo output. This is distinct from floppy DMA, even though both involve transferring data from RAM without the CPU copying every byte.

#### 5. Why this matters to programming

The STE is mostly compatible with ST software, but its video hardware timing and scrolling features differ. Programs that rely on very precise ST video timing—especially demos that manipulate display borders or change registers at specific scanline positions—may behave differently on an STE. 

The key architectural takeaway is:

- **ST:** GLUE and MMU cooperate as separate chips.
- **STE:** GSTMCU combines their responsibilities.
- **Both:** the 68000 remains the CPU, and the disk DMA controller remains a distinct component.
- **STE addition:** the enhanced Shifter and memory-control logic also support stereo digital audio and finer scrolling.

---

## Turn 16 — 2026-10-28 06:58 UTC

### Question

> how many physical oscillators/clocks does the atari st have. and how are these clock signals distributed to the different components

### Answer

The original Atari ST has **one main high-frequency clock source for the video and system timing chain**, plus separate crystal-based timing sources for serial communication and colour encoding. The exact number of physical oscillators or crystals depends on the model and video standard.

The key point is that the familiar 32, 16, 8, 4, 2 and 0.5 MHz signals are **not six independent oscillators**. Most are derived from one master clock and divided down by the Shifter, MMU and GLUE. The original ST service manual and schematics show this distribution. 

#### 1. The main clock-distribution chain

<box border radius="xl" padding={3} gap={2} align="center">
  <box border radius="md" padding={3} width="100%" align="center">
    <title size="lg">Main master source</title>
    <title size="xl" color="default">≈ 32 MHz</title>
    <text color="secondary" size="sm">Crystal / clock-buffer circuit</text>
  </box>
  <icon name="arrow-down" size="lg" color="secondary" />
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **Video Shifter**

    <text size="sm">Uses the high-frequency clock for pixel timing and provides the 16 MHz clock to the MMU</text>
  </box>
  <icon name="arrow-down" size="lg" color="secondary" />
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **MMU / memory controller — 16 MHz input**

    <text size="sm">Generates the 8 MHz and 4 MHz system clocks</text>
  </box>
  <icon name="arrow-down" size="lg" color="secondary" />
  <box background="surface-secondary" radius="md" padding={3} width="100%" align="center">
    **8 MHz system clock**

    <text size="sm">68000 CPU, GLUE, DMA controller and WD1772 floppy controller</text>
  </box>
  <icon name="arrow-down" size="lg" color="secondary" />
  <box border radius="md" padding={3} width="100%" align="center">
    **GLUE clock dividers**

    <row gap={2} width="100%">
      <box background="surface" border radius="md" padding={2} flex="1" align="center">
        <title size="lg">2 MHz</title>
        <text size="xs" textAlign="center">YM2149 sound chip</text>
      </box>
      <box background="surface" border radius="md" padding={2} flex="1" align="center">
        <title size="lg">500 kHz</title>
        <text size="xs" textAlign="center">Keyboard and MIDI ACIAs</text>
      </box>
    </row>
  </box>
</box>

This is the main synchronous clock tree; the clock frequencies are related, but the chips use them for different purposes. 

#### 2. Other physical clock sources

The main clock tree is not the whole story. Some peripherals need frequencies that are independent of the main system clock.

| Physical source | Typical frequency | Used for |
|---|---:|---|
| Main master crystal/clock circuit | About 32 MHz | Video timing and the main system clock tree |
| MFP baud-rate crystal | 2.4576 MHz | Accurate serial baud-rate timing inside the MC68901 MFP |
| Colour-subcarrier crystal, on STFM/video-modulator versions | 4.433618 MHz (PAL) or 3.579545 MHz (NTSC) | Colour encoding for the RF/composite video circuitry |

The MFP also receives the **4 MHz system clock**. Its separate 2.4576 MHz crystal is not a replacement for that clock; it serves the serial timing circuitry. The colour-subcarrier source is model-dependent, so not every ST motherboard has it. 

#### 3. Where each derived clock goes

<box border radius="lg" padding={3} gap={3}>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} minWidth="84px" align="center">
      <title size="lg">16 MHz</title>
    </box>
    <box flex="1" gap={1}>
      **MMU / memory controller**

      <text size="sm">Coordinates DRAM timing and memory access, including arbitration between CPU and video access.</text>
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} minWidth="84px" align="center">
      <title size="lg">8 MHz</title>
    </box>
    <box flex="1" gap={1}>
      **68000, GLUE, DMA, WD1772**

      <text size="sm">The main system clock used by the CPU and several system-control and peripheral chips.</text>
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} minWidth="84px" align="center">
      <title size="lg">4 MHz</title>
    </box>
    <box flex="1" gap={1}>
      **MFP system clock input**

      <text size="sm">Clocks the MFP's general internal operations; the separate crystal supports its serial baud-rate generator.</text>
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} minWidth="84px" align="center">
      <title size="lg">2 MHz</title>
    </box>
    <box flex="1" gap={1}>
      **YM2149 PSG**

      <text size="sm">Clocks the programmable sound generator.</text>
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} minWidth="84px" align="center">
      <title size="lg">500 kHz</title>
    </box>
    <box flex="1" gap={1}>
      **Two MC6850 ACIAs**

      <text size="sm">Provides timing for the keyboard and MIDI serial interfaces.</text>
    </box>
  </row>
</box>

These frequencies are documented in the original ST clock-distribution schematic. 

#### 4. So how many clocks does it have?

The answer depends on what you mean by “clock”:

- **Independent physical frequency sources:** commonly around two or three on an ST/STFM motherboard, depending on the model and video circuitry.
- **Derived system clock frequencies:** several — including 16, 8, 4, 2 and 0.5 MHz.
- **Clock inputs to individual chips:** more than the number of sources, because the derived signals are distributed to many components.

One extra wrinkle: the **STE adds a separate approximately 8.0106 MHz oscillator for its enhanced audio/video clocking**, used to derive the DMA sound sample rates. Its GSTMCU supplies the main derived system clocks. 

The most useful mental model is: **a small number of physical sources feed a chain of dividers and clock outputs; the resulting clocks are distributed to the chips that need them.**

---

## Turn 17 — 2026-10-28 07:05 UTC

### Question

> give me the exhautive list of physical ports and connectotrs of the atari ste machine

### Answer

### Atari STE: exhaustive list of physical ports and connectors

The Atari 520STE and 1040STE have essentially the same external connector set. The main differences between models are RAM capacity and case/board details, rather than the types of ports.

I’ll cover the **external connectors on the computer**, then the internal connectors and expansion interfaces that matter if you’re servicing or upgrading the machine. I’ll distinguish standard STE features from connectors that vary by motherboard revision or require an adapter.

#### 1. External connectors

The table below covers the standard 520STE/1040STE case. Connector positions are described by panel, since left/right orientation can be confusing when viewing the computer from the front or rear. 

##### Rear panel

<box gap={3}>
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE rear panel RS232 printer ACSI external floppy ports close up" aspectRatio="4:3" width="36%" />
    <box flex="1" gap={1}>
      **1. RS-232 serial / modem**

      <badge>DB-25 male</badge>

      For serial devices such as modems and compatible serial peripherals. It uses the MFP UART and RS-232 line-driver circuitry.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Atari 1040STE printer parallel DB25 connector rear panel" aspectRatio="4:3" width="36%" />
    <box flex="1" gap={1}>
      **2. Parallel printer port**

      <badge>DB-25 female</badge>

      Primarily for Centronics-compatible parallel printers and other compatible parallel devices. The YM2149's GPIO lines participate in the interface.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE ACSI hard disk DMA DB19 connector rear panel" aspectRatio="4:3" width="36%" />
    <box flex="1" gap={1}>
      **3. ACSI / DMA port**

      <badge>DB-19 female</badge>

      For external ACSI hard disks and compatible peripherals, including certain Atari laser-printer configurations. This is Atari's DMA-oriented storage interface, not a standard PC SCSI connector despite some similarities.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE 14 pin DIN external floppy connector rear panel" aspectRatio="4:3" width="36%" />
    <box flex="1" gap={1}>
      **4. External floppy drive**

      <badge>14-pin DIN</badge>

      Connects a compatible external floppy drive. The connector carries drive selection, step/direction, read/write and drive-status signals.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Atari 1040STE rear RCA television RF output connector" aspectRatio="4:3" width="36%" />
    <box flex="1" gap={1}>
      **5. Television output**

      <badge>RCA / phono</badge>

      RF television output from the built-in modulator on equipped models. It is not the same as the composite-video signal available on the monitor connector. Some regional versions, notably the French Peritel version, differ.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Atari ST STE 13 pin DIN monitor connector rear panel" aspectRatio="4:3" width="36%" />
    <box flex="1" gap={1}>
      **6. Monitor**

      <badge>13-pin DIN</badge>

      Carries RGB video, horizontal and vertical sync, monochrome video, composite video and audio-related signals. The monitor type and wiring determine which signals are used.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Atari 1040STE stereo audio RCA output sockets rear panel" aspectRatio="4:3" width="36%" />
    <box flex="1" gap={1}>
      **7–8. Stereo audio outputs**

      <badge>2 × RCA</badge>

      Separate left and right line-level outputs for the STE's stereo DMA sound. Connect to an amplifier, mixer or powered speakers.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Atari 1040STE IEC C14 mains power inlet rear panel" aspectRatio="4:3" width="36%" />
    <box flex="1" gap={1}>
      **9. Mains power inlet**

      <badge>IEC 60320 C14</badge>

      Accepts a standard IEC mains cable. The STE has an internal power supply, unlike some early ST models that used an external power brick.
    </box>
  </row>
</box>

The rear panel also has a **power switch** and a **reset button**. These are physical controls, not connectors. The television-output connector may be absent or different on regional variants. 

##### Left side

<box gap={3}>
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE left side 40 pin cartridge port MIDI connectors" aspectRatio="4:3" width="36%" />
    <box flex="1" gap={1}>
      **10. ROM cartridge slot**

      <badge>40-pin edge connector</badge>

      Accepts ROM cartridges, diagnostics and compatible expansion cartridges. It exposes a cartridge-accessible part of the system bus; it is not a general-purpose modern expansion slot.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE MIDI IN MIDI OUT 5 pin DIN connectors" aspectRatio="4:3" width="36%" />
    <box flex="1" gap={1}>
      **11. MIDI IN**

      <badge>5-pin DIN</badge>

      Receives MIDI data from keyboards, synthesizers, sequencers and other MIDI equipment.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE MIDI OUT 5 pin DIN connector" aspectRatio="4:3" width="36%" />
    <box flex="1" gap={1}>
      **12. MIDI OUT**

      <badge>5-pin DIN</badge>

      Sends MIDI data. The output circuitry can also support MIDI THRU arrangements, depending on the equipment or wiring used.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Atari 1040STE enhanced joystick paddle ports 15 pin D sub left side" aspectRatio="4:3" width="36%" />
    <box flex="1" gap={1}>
      **13–14. Enhanced controller ports A and B**

      <badge>2 × 15-pin high-density D-sub</badge>

      These are the STE-specific enhanced game-controller interfaces. They support compatible analog paddles and other special controllers, including light-pen/light-gun-type peripherals designed for the port.
    </box>
  </row>
</box>

##### Under the front edge of the case

<box gap={3}>
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE mouse joystick DE9 ports underside front case" aspectRatio="4:3" width="36%" />
    <box flex="1" gap={1}>
      **15. Mouse / joystick 0**

      <badge>DE-9 male</badge>

      Normally used for the mouse, but it can accept compatible joystick devices.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE joystick 1 DE9 connector underside front" aspectRatio="4:3" width="36%" />
    <box flex="1" gap={1}>
      **16. Joystick 1**

      <badge>DE-9 male</badge>

      Standard digital joystick input, commonly used for games.
    </box>
  </row>
</box>

The STE therefore has **16 external connector positions when the television/RF output is fitted**, or 15 if that output is absent. The built-in keyboard and internal floppy drive do not count as external ports. 

#### 2. Internal connectors and sockets

If by “exhaustive” you also mean the connectors inside the case, these are the main ones to know.

| Internal connector / socket | Typical form | Purpose |
|---|---|---|
| Internal floppy-drive cable | 34-pin ribbon connector | Connects the built-in 3.5-inch floppy drive to the motherboard's floppy interface |
| Keyboard harness | Ribbon/cable connector | Connects the keyboard assembly to the motherboard |
| RAM sockets | Four 30-pin SIMM sockets on standard boards | Holds RAM modules; the 520STE normally uses one bank, while the 1040STE has two |
| TOS ROM sockets | Two 32-pin IC sockets | Holds the two ROM devices containing the operating system |
| Power-supply wiring | Board-specific power connectors | Supplies the motherboard and drive |
| Main-chip sockets | Board/revision-dependent IC sockets | Service or replacement access for selected chips, such as the CPU and Blitter on common boards |

The 520STE and 1040STE service manual documents the RAM sockets, ROMs, drive, motherboard and power-supply layout. Very early machines may use SIPP memory modules instead of SIMMs, so the precise internal configuration depends on the board revision. 

There is **no standard PC-style PCI, ISA or general-purpose internal expansion slot** on the ordinary STE motherboard. The cartridge connector is the main built-in expansion interface; other upgrades typically use the cartridge port, a bus adapter, or board-level modifications.

#### 3. Connector types at a glance

| Connector | Count | Main use |
|---|---:|---|
| DB-25 male | 1 | RS-232 serial |
| DB-25 female | 1 | Parallel printer |
| DB-19 female | 1 | ACSI/DMA storage |
| 14-pin DIN | 1 | External floppy |
| 13-pin DIN | 1 | Monitor |
| RCA / phono | 3 normally | TV/RF plus left/right audio |
| IEC C14 | 1 | Mains power |
| 5-pin DIN | 2 | MIDI IN and OUT |
| 40-pin edge connector | 1 | Cartridge |
| 15-pin high-density D-sub | 2 | Enhanced controllers |
| DE-9 male | 2 | Mouse and joystick |

The counts above describe the common configuration with an RF/TV output; regional variants can change that one entry.

If you want to identify or wire a particular port next, the most useful reference is the Atari STE field service manual, which includes connector pinouts for the monitor, game controllers, MIDI, serial, parallel, floppy and ACSI interfaces. <Link url="https://retrotechwiki.com/Atari_520STE/1040STE_Field_Service_Manual" title="Atari 520STE/1040STE Field Service Manual"/>.

---

## Turn 18 — 2026-10-28 07:20 UTC

### Question

> i am interested in the french variant of the ste with the peritel ouput. were there many board revisions

### Answer

Yes — there were **multiple STE motherboard revisions**, but the French Peritel version is best understood as a *regional variant of the STE*, not a completely separate computer design.

Atari's STE service manual explicitly identifies a motherboard **without the RF modulator for the French Peritel version**. That is the most important distinction when identifying the French model. 

#### What varied between STE boards?

The documented board families include **CA400329-101** and the related assembly/PCB numbers used in Atari's parts lists. Community-maintained board references also record boards marked **CA4003290**, although that marking format and the service-manual assembly numbers are not always written consistently. 

| What changed | Why it matters |
|---|---|
| RF modulator omitted | French Peritel models use a direct video-output arrangement rather than the normal RF modulator |
| RAM configuration | 520STE and 1040STE have different factory RAM populations; very early units may differ in module type |
| DMA controller revision | Some later STE boards use a different DMA IC revision, which can matter for storage compatibility |
| ROM version | Early units commonly shipped with TOS 1.06, followed by TOS 1.62; ROM version is not itself a motherboard revision |
| Component placement and board layout | Production changes can alter chip packages, sockets, jumpers and service procedures |

The STE was introduced in France in 1989, before its broader UK release in 1990. That makes French-market machines particularly interesting when studying early production hardware. 

#### How many revisions were there?

I could verify that there were several STE board variants, but I **couldn't establish a reliable exhaustive count specifically for French Peritel boards** from the available documentation. The service manual is mainly a general STE reference, and its detailed parts list is for a US 1040STE with an RF modulator. It does not provide a complete revision history for French production. 

If you want to identify your particular machine, the most useful next step is to inspect the motherboard's printed assembly number and revision marking. The PCB identifier is usually more reliable than the case label or serial number alone.

**If you send a clear photo of the motherboard (especially the printed PCB number and revision), I can help identify which revision it is and what is distinctive about it.**

---

## Turn 19 — 2026-10-28 07:25 UTC

### Question

> in my study, i would like to settle on the most sold revision if the ste motherbord, not an early model, not the latest revision, but the volume produced motherboard for the french market with tos 1.62, simm memory, peritel output

### Answer

For your study, I would choose a **mature-production Atari STE motherboard from the CA4003290 family**, with TOS 1.62, conventional 30-pin SIMM sockets and the French PAL/Peritel video configuration — rather than an early 1989 board or the late CA401177 design.

The reason is that the CA4003290 family is identified as the more common STE motherboard in modern hardware-maintenance references, while CA401177 is associated with a later production design. However, I couldn't find factory production statistics proving which individual revision sold the most in France. 

#### The configuration I would target

| Attribute | Target |
|---|---|
| Computer | Atari 520STE or 1040STE |
| Motherboard family | **CA4003290**, mature production |
| Production period | Approximately 1990–1991 as a practical target, not a verified French sales peak |
| Operating system | TOS 1.62, original ROMs |
| RAM | 30-pin SIMM sockets, not early SIPP-style memory |
| Video | French PAL configuration, with the appropriate Peritel/SCART connection |
| Board to avoid for your purpose | CA401177 late-production design |
| Also avoid | Early CA4003290 boards with factory bodge wires or early SIPP sockets |

The CA4003290 identifier alone is not enough to identify a precise revision: several production iterations used that family marking, with changes to chip mounting, the Blitter arrangement and other circuitry. 

#### One important detail about Peritel

Don't use the Peritel connection alone to identify the motherboard. The STE's video interface and the regional cable or rear-panel configuration are separate things. For a historically accurate French setup, verify the actual rear-panel connector and video wiring, as well as whether the RF modulator is fitted.

#### My recommendation

Use a **CA4003290 board with TOS 1.62, ordinary SIMM sockets and no obvious factory bodge-wire modifications** as your baseline. It's a defensible representative mid-production STE configuration, even though I can't honestly label it the statistically most-sold French revision without production records.

For the study, I would document the exact PCB markings, date code, ROM labels, DMA chip marking, RAM socket type and video-output arrangement. Those details will make the chosen specimen reproducible and help distinguish its production iteration from other CA4003290 boards.

---

## Turn 20 — 2026-10-28 07:29 UTC

### Question

> i have no real machine, i want to settle on a specific motherboard revision to develop an emulator, what would be the differences between CA4003290 motherboard revisions

### Answer

For an emulator, I would treat the **CA4003290 as a family of STE motherboard implementations, not one single fixed revision**. The important distinction is that the revisions appear to change component packaging, board-level fixes and some DMA-related circuitry—not the overall software-visible STE architecture.

One complication: the commonly quoted “first, second and third revisions” are largely a community classification based on board photographs and component changes. I haven’t found a complete Atari revision history that formally specifies every change and its exact production cutoff. The most useful detailed guide is Exxos’s [STE DMA Survival Guide](https://www.exxosforum.co.uk/atari/last/DMASG/index.htm). 

#### The CA4003290 variants

<box gap={3}>
  <row align=start gap={3}>
    <AsyncImage query="Atari STE CA4003290 early motherboard bodge wires black SIMM sockets" aspectRatio="4:3" width="124px" />
    <box flex="1" gap={1}>
      **1. Early 1989 boards (“beta” in the community guide)**

      - Some have factory bodge wires and a chip mounted on top of another chip.
      - Early memory configurations can use SIPP-style modules or black SIMM sockets.
      - Component changes suggest Atari was still correcting and refining the design.

      <text color="secondary" size="sm">Emulator relevance: mainly board-level electrical fixes and possible DMA/bus timing differences, rather than a different programming model.</text>
    </box>
  </row>
  <divider color="subtle" />
  <row align=start gap={3}>
    <AsyncImage query="Atari STE CA4003290 motherboard second revision soldered integrated circuit SIPP sockets" aspectRatio="4:3" width="124px" />
    <box flex="1" gap={1}>
      **2. Intermediate revision**

      - A chip that was piggybacked on the earlier version is mounted directly on the PCB.
      - Some examples still retain SIPP-style memory sockets, so memory-socket style alone does not reliably identify the revision.
      - This is evidence of a board-production change, but not by itself proof of changed CPU-visible behaviour.

      <text color="secondary" size="sm">Emulator relevance: likely low unless the change affects a signal, timing path or peripheral edge case.</text>
    </box>
  </row>
  <divider color="subtle" />
  <row align=start gap={3}>
    <AsyncImage query="Atari STE CA4003290 motherboard third revision surface mounted GST chip no separate blitter" aspectRatio="4:3" width="124px" />
    <box flex="1" gap={1}>
      **3. Later CA4003290 variant**

      - The discrete Blitter is no longer present as a separate chip; its functionality is integrated into the large surface-mounted custom chip.
      - The DMA Survival Guide associates this era with the possible introduction of the revised DMA chip, marked **C398739-001A**, but cautions that the exact transition is hard to establish.
      - Chip replacement during repairs means a board’s current DMA chip may not be the one installed at the factory.

      <text color="secondary" size="sm">Emulator relevance: the most interesting variant to investigate for DMA compatibility and Blitter implementation details.</text>
    </box>
  </row>
</box>

These distinctions are based on the community guide’s visual classification; the exact boundaries between batches are not firmly documented. 

#### What actually matters to an emulator?

| Difference | Likely emulator impact | What I would do |
|---|---|---|
| Bodge wires / resistor-pack values | Potential bus-arbitration stability or timing differences | Model accurate bus ownership and DMA handoff; investigate only if tests expose a difference |
| SIPP versus SIMM sockets | None to normal software; both provide RAM | Model RAM size and memory map, not the physical socket |
| Discrete versus integrated Blitter implementation | Potentially relevant if internal timing or register behaviour differs | Implement the documented Blitter interface; validate busy timing and bus contention |
| DMA chip revision | Potentially important for floppy DMA compatibility and corner cases | Keep DMA behaviour modular and test against multiple DMA chip variants |
| TOS 1.06 versus 1.62 | Directly affects software behaviour and bug compatibility | Use TOS 1.62 as your default ROM target |
| French PAL / Peritel configuration | Affects video standard and physical output, not the core CPU instruction set | Target 50 Hz PAL timings; treat SCART/Peritel as an output connection, not a motherboard revision |

The DMA chip deserves particular attention: the Exxos guide documents Atari changes to resistor packs associated with the `BR`, `BG` and `BGACK` bus-arbitration signals, as well as changes to address-bus pull-ups. Those details are a reason to take DMA and bus timing seriously, but they don't establish that every CA4003290 revision behaves differently in every software-visible situation. 

#### My recommended target

If I were starting your emulator from scratch, I would define the target as:

<box border radius="lg" padding={3} gap={2}>
  <title size="lg">Atari 1040STE — mature CA4003290 configuration</title>
  <row align="center" justify="between">
    Motherboard family
    <text weight="medium">CA4003290</text>
  </row>
  <divider color="subtle" />
  <row align="center" justify="between">
    TOS
    <text weight="medium">1.62</text>
  </row>
  <divider color="subtle" />
  <row align="center" justify="between">
    Memory
    <text weight="medium">4 MB SIMM configuration</text>
  </row>
  <divider color="subtle" />
  <row align="center" justify="between">
    Video
    <text weight="medium">PAL, 50 Hz</text>
  </row>
  <divider color="subtle" />
  <row align="center" justify="between">
    CPU
    <text weight="medium">8 MHz 68000</text>
  </row>
  <divider color="subtle" />
  <row align="center" justify="between">
    Custom hardware
    <text weight="medium">STE GSTMCU, enhanced Shifter, DMA</text>
  </row>
  <text color="secondary" size="sm">This is a practical emulator target, not a claim that one precisely identified CA4003290 subrevision was the highest-volume French production board.</text>
</box>

I would **not** make the emulator depend on one uncertain subrevision’s exact resistor values or board wiring at the start. Build a solid baseline around the documented STE architecture, then make DMA timing and any confirmed revision-dependent behaviour selectable. The CA4003290 family is commonly encountered, and TOS 1.62 is a well-established STE configuration. 

One important point: **TOS 1.62 and French Peritel output do not uniquely identify a CA4003290 subrevision.** Treat the ROM version, PAL video configuration and motherboard implementation as separate configuration choices.

##### A useful development strategy

1. Implement the documented STE hardware and TOS 1.62 baseline.
2. Make bus arbitration, DMA transfers, Blitter activity and video fetch timing cycle-aware where needed.
3. Run established Atari ST/STE compatibility tests and real software test suites.
4. Only add a board-revision switch when you have evidence of a real observable difference—rather than reproducing every physical production change speculatively.

Since you don't own hardware, the most valuable next step would be to obtain **high-resolution front-and-back photos of a specific CA4003290 board, along with its chip markings and date codes**. That would let us define a more precise reference configuration and distinguish visible manufacturing changes from changes an emulator actually needs to reproduce.

---

## Turn 21 — 2026-10-28 07:36 UTC

### Question

> search and label a high resolution mature CA4003290 board photo

### Answer

I found two useful references. The best starting point is the **CA4003290 revision comparison** in Exxos’s STE DMA guide: it shows the early, intermediate and later CA4003290 layouts side by side. For a higher-resolution individual board photograph, the Atari Wiki hosts a **1,140 × 683** image specifically identified as a CA4003290 motherboard. 

- [Open the CA4003290 high-resolution photo](https://www.atari-wiki.com/index.php?title=File:ST_Motherboard_CA4003290_Rev_stefan_jl.jpg)
- [Open the STE revision comparison](https://exxosforum.co.uk/atari/last/DMASG/index.htm)

#### Recommended reference photo: later CA4003290

<box gap={2}>
  <AsyncImage query="site:exxosforum.co.uk/atari/last/DMASG ste3.jpg Atari STE CA4003290 motherboard" aspectRatio="5:3" width="100%" />
  <text color="secondary" size="sm">Source: Exxos STE DMA Survival Guide, image identified as the third CA4003290 revision. </text>
  [Open the original full-size photo](https://www.exxosforum.co.uk/atari/last/DMASG/ste3.jpg)
</box>

##### Labels to use when studying the photo

1. **Board identity** — look along the bottom edge for the `CA4003290` marking.
2. **Memory sockets** — the long SIMM socket area in the upper-middle portion.
3. **Integrated Blitter** — the later design does not have the separate Blitter IC seen on earlier boards; its functionality is integrated into the large surface-mounted custom chip.
4. **DMA chip** — identify it by its printed part number rather than position alone; the guide discusses both `C025913-38` and `C398739-001A`.
5. **Factory wiring and resistor packs** — inspect the central and lower board areas for wiring changes and bus pull-up resistor packs.

**Why I would choose this photo:** it represents the later CA4003290 layout rather than the earliest board with the piggybacked chip. The guide calls it the third revision, although that numbering is the author’s classification, not a confirmed Atari revision standard. It does not, by itself, prove the exact French-market configuration or factory TOS version.

I found the original high-resolution photograph, but I can’t reliably draw and verify annotation overlays on that source image in this response. The source image and numbered legend above identify the reference without risking misleading labels on individual chips.

---

## Turn 22 — 2026-10-28 07:41 UTC

### Question

> summarize the ste revision comparison

### Answer

The [STE DMA Survival Guide](https://www.exxosforum.co.uk/atari/last/DMASG/index.htm) describes four main motherboard designs: three variants of the **CA4003290** and the later **CA401177**. The numbering is the guide author's classification, not a confirmed official Atari revision scheme. 

| Board variant | Main visible differences | Emulator significance |
|---|---|---|
| **CA4003290 — early (1989)** | Factory bodge wires, one chip piggybacked on another; some boards have SIPP memory sockets or black SIMM sockets | Possible bus-timing and DMA stability differences |
| **CA4003290 — second** | Previously piggybacked chip is mounted directly on the motherboard; some still have SIPP sockets | Mostly physical changes unless they affect signal timing |
| **CA4003290 — third** | Separate Blitter chip removed; Blitter functionality integrated into the large surface-mounted custom chip. Revised DMA chip may appear around this period | Most interesting CA4003290 variant to investigate for DMA compatibility |
| **CA401177 — later design (1991)** | More extensive surface mounting, including CPU, Shifter and DMA; fewer sockets | Distinct later hardware implementation |

##### Other changes across production

Atari also changed resistor packs used for bus pull-ups, including circuitry involved in CPU/DMA bus handoff. These changes may matter more to electrical stability than the visible socket differences. The guide cautions that the DMA chip marked `C398739-001A` is not a universal fix for storage corruption, and that not every later board necessarily has it installed from the factory. 

**My recommendation for your emulator:** target the third CA4003290 as a practical baseline, with TOS 1.62 and PAL timing, but keep DMA behaviour configurable. Don't emulate SIPP sockets, bodge wires or resistor values unless you later find evidence that they produce a measurable software-visible difference.

---

## Turn 23 — 2026-10-28 07:44 UTC

### Question

> explain in great details the ste video subsystem

### Answer

### Atari STE video subsystem: a detailed hardware guide

The Atari 1040STE video subsystem is best understood as a **real-time pipeline involving RAM, the MMU, the enhanced Shifter, the GSTMCU, and the monitor interface**. It is not simply a framebuffer that the CPU draws into and the video chip reads whenever it wants.

For emulator development, the key is understanding three separate things:

1. **Video memory:** where the pixels and palette indices are stored.
2. **Video timing:** when the hardware fetches each piece of image data and emits each pixel.
3. **Display control:** how resolution, palette, scrolling, synchronization and monitor signals are generated.

The STE preserves the broad video architecture of the original ST while adding important enhancements: a 4096-colour palette, fine horizontal scrolling, and a few video timing and display-control differences. Its video subsystem remains tightly coupled to memory arbitration, so accurate timing can matter even when the displayed image looks correct.

#### 1. The chips and their responsibilities

<box gap={2} align="center">
  <box border radius="lg" padding={3} width="100%" align="center" gap={1}>
    <title size="md">Main RAM</title>
    <text textAlign="center" color="secondary" size="sm">Contains packed bitplanes: the image's pixel data</text>
  </box>
  <box align="center" gap={1}>
    <icon name="arrow-down" size="xl" />
    <text color="secondary" size="xs">Video memory fetches</text>
  </box>
  <box border radius="lg" padding={3} width="100%" align="center" gap={1}>
    <title size="md">MMU / memory controller</title>
    <text textAlign="center" color="secondary" size="sm">Schedules video RAM accesses, maintains the video address counter and controls DRAM access</text>
  </box>
  <box align="center" gap={1}>
    <icon name="arrow-down" size="xl" />
    <text color="secondary" size="xs">Fetched words</text>
  </box>
  <box border radius="lg" padding={3} width="100%" align="center" gap={1}>
    <title size="md">Enhanced Shifter</title>
    <text textAlign="center" color="secondary" size="sm">Loads bitplane data, forms pixel indices, applies fine-scroll behaviour and looks up palette colours</text>
  </box>
  <box align="center" gap={1}>
    <icon name="arrow-down" size="xl" />
    <text color="secondary" size="xs">Digital colour values</text>
  </box>
  <box border radius="lg" padding={3} width="100%" align="center" gap={1}>
    <title size="md">RGB output circuitry</title>
    <text textAlign="center" color="secondary" size="sm">Converts palette values into analogue red, green and blue signals</text>
  </box>
</box>

Alongside this pipeline is the **GSTMCU**, which combines functions previously handled by separate ST MMU and GLUE chips. Its memory-control functions and video-timing logic coordinate the display with the rest of the system. The enhanced Shifter is still a distinct functional block.

The division of responsibility is important:

- **MMU / memory controller:** determines when video data is fetched and manages access to DRAM.
- **GSTMCU:** handles system timing and video-control functions, including display enable and synchronization.
- **Shifter:** transforms fetched bits into pixel values and generates the colour output.

This is broadly the ST video architecture enhanced for the STE, not an independent graphics processor with its own private framebuffer. 

#### 2. The three standard display modes

The STE retains the three standard ST display modes.

<box gap={3}>
  <row align=start gap={3}>
    <AsyncImage query="Atari ST STE low resolution 320x200 16 color game screenshot" aspectRatio="4:3" width="124px" />
    <box flex="1" gap={1}>
      **Low resolution — 320 × 200**

      - 320 pixels horizontally and 200 visible lines.
      - 4 bitplanes per pixel.
      - \(2^4 = 16\) palette entries can be selected for each pixel.
      - Each pixel is represented by four bits, distributed across four planes.

      This is the main colour mode used by many games and demos.
    </box>
  </row>
  <divider color="subtle" />
  <row align=start gap={3}>
    <AsyncImage query="Atari ST STE medium resolution 640x200 four color GEM desktop screenshot" aspectRatio="4:3" width="124px" />
    <box flex="1" gap={1}>
      **Medium resolution — 640 × 200**

      - 640 horizontal pixels and 200 visible lines.
      - 2 bitplanes per pixel.
      - \(2^2 = 4\) simultaneously selectable palette entries.

      It doubles horizontal resolution relative to low resolution but has fewer simultaneously selectable colours.
    </box>
  </row>
  <divider color="subtle" />
  <row align=start gap={3}>
    <AsyncImage query="Atari ST STE high resolution 640x400 monochrome GEM desktop screenshot" aspectRatio="4:3" width="124px" />
    <box flex="1" gap={1}>
      **High resolution — 640 × 400**

      - 640 × 400 monochrome pixels.
      - 1 bitplane.
      - Designed for the dedicated monochrome monitor mode.

      The pixel stream bypasses normal colour-palette selection for monochrome output. This mode uses a different display timing configuration.
    </box>
  </row>
</box>

The colour modes normally run at approximately 50 Hz with PAL-compatible timing or 60 Hz with NTSC-compatible timing. The monochrome mode uses a higher refresh rate, approximately 71 Hz. A French-market PAL STE emulator should begin with the 50 Hz colour configuration. 

##### How many bytes does a screen use?

For a conventional 320 × 200 low-resolution screen:

\[
320 \times 200 \times 4 \div 8 = 32{,}000\text{ bytes}
\]

That is 32,000 bytes, or 31.25 KiB, for the visible pixel data. Medium resolution also uses 32,000 bytes because it has twice as many pixels but half as many bits per pixel.

High resolution requires:

\[
640 \times 400 \div 8 = 32{,}000\text{ bytes}
\]

So all three standard modes have the same nominal visible bitmap size. Real display fetches, border regions and scrolling can affect the memory traffic beyond the bytes representing visible pixels.

#### 3. Bitplanes: how pixels are encoded in RAM

The STE does not normally store a colour pixel as a byte containing a palette index. Instead, the screen is encoded in **bitplanes**.

Imagine a low-resolution pixel whose four plane bits are:

- Plane 0: `1`
- Plane 1: `0`
- Plane 2: `1`
- Plane 3: `1`

Together, those bits form the four-bit index `1101` (13). The Shifter uses index 13 to select palette register 13, whose RGB value determines the output colour.

For a 16-pixel group in low resolution, one 16-bit word from each of the four bitplanes supplies one bit for each of the 16 pixels.

<box border radius="lg" padding={3} gap={2}>
  <title size="md">One 16-pixel group in low resolution</title>
  <grid columns={4} gap={1}>
    {#each ["Plane 0","Plane 1","Plane 2","Plane 3"] as p}
      <grid-item>
        <box background="surface-secondary" radius="md" padding={2} align="center">
          <text weight="medium" size="sm">{p}</text>
          <text color="secondary" size="xs">16 bits</text>
        </box>
      </grid-item>
    {/each}
  </grid>
  <box align="center" gap={1}>
    <icon name="arrow-down" size="lg" />
    <text color="secondary" size="sm">Combine corresponding bit positions</text>
  </box>
  <box background="surface" border radius="md" padding={2} align="center">
    **16 pixel indices, each 4 bits wide**
  </box>
  <box align="center" gap={1}>
    <icon name="arrow-down" size="lg" />
    <text color="secondary" size="sm">Palette lookup</text>
  </box>
  <box background="surface" border radius="md" padding={2} align="center">
    **16 RGB colour values**
  </box>
</box>

The Shifter serialises those pixel values into the video output stream. The exact grouping and number of words fetched per display interval depend on the resolution. Medium resolution uses two planes, and monochrome uses one.

For emulator development, keep these operations separate:

1. Fetch the words from video memory.
2. Interpret their bit positions according to the current resolution.
3. Construct pixel indices.
4. Resolve the indices through the palette, except in monochrome mode.
5. Emit pixels at the appropriate video timing.

That separation makes it easier to add fine scrolling, palette changes during a scanline and mid-screen display effects without rewriting the entire renderer. 

#### 4. The STE colour palette: 4096 possible colours

The original ST palette could represent 512 distinct colours: three bits for red, three for green and three for blue, giving \(8^3=512\).

The STE expands this to **four bits per channel**, producing:

\[
16 \times 16 \times 16 = 4096
\]

The important distinction is that 4096 is the number of colours the palette can represent, not the number of colours normally visible at once in a standard mode. Low resolution can select 16 palette entries, medium resolution four, and high resolution is monochrome.

The STE has 16 palette registers, mapped from `$FFFF8240` through `$FFFF825E` in two-byte steps. Each register holds a 12-bit RGB value within a 16-bit word.

<box border radius="lg" padding={3} gap={2}>
  <title size="md">STE palette word layout</title>
  <grid columns={3} gap={1}>
    <grid-item>
      <box background="#B91C1C" radius="md" padding={2} align="center">
        <text color="#FFFFFF" weight="medium">Red</text>
        <text color="#FFFFFF" size="sm">4 bits</text>
      </box>
    </grid-item>
    <grid-item>
      <box background="#15803D" radius="md" padding={2} align="center">
        <text color="#FFFFFF" weight="medium">Green</text>
        <text color="#FFFFFF" size="sm">4 bits</text>
      </box>
    </grid-item>
    <grid-item>
      <box background="#1D4ED8" radius="md" padding={2} align="center">
        <text color="#FFFFFF" weight="medium">Blue</text>
        <text color="#FFFFFF" size="sm">4 bits</text>
      </box>
    </grid-item>
  </grid>
  <text color="secondary" size="sm">Conceptually: `0000 RRRR GGGG BBBB` — the upper four bits are unused, followed by red, green and blue.</text>
</box>

For example, a palette value of `$0FFF` selects maximum red, green and blue, yielding white. `$0000` is black, and `$0F00` is maximum red.

The STE's extra intensity bit is placed above the three original ST bits for each channel, preserving compatibility with the older palette format. An emulator must decode the STE format correctly rather than interpreting its register values as the original nine-bit ST palette. 

##### Why palette timing matters

A palette register is not merely a property of the entire frame. Software can change it while the display is being drawn.

For example, a program may display a blue sky in the upper part of the screen, then change palette entries during the frame so the same indices represent orange and red in the lower part. This creates more colours across a complete image than the nominal 16 simultaneously selectable entries might suggest.

For an emulator, applying the final palette uniformly to the entire frame is therefore insufficient. The palette values used for each pixel must reflect when the relevant register writes take effect. Exact mid-scanline effects require timing-aware writes and should be added after the normal palette and scanline pipeline works.

#### 5. Video memory addressing

The STE's display memory can be relocated in RAM. The CPU programs a video base address, and the memory controller advances a video address counter as words are fetched.

The principal registers are:

| Address | Register | Purpose |
|---|---|---|
| `$FFFF8201` | Video base high | Upper byte of the programmed display base |
| `$FFFF8203` | Video base middle | Middle byte of the programmed display base |
| `$FFFF820D` | Video base low | STE extension giving finer base-address placement |
| `$FFFF8205` | Video counter high | High byte of the current video fetch address |
| `$FFFF8207` | Video counter middle | Middle byte of the current video fetch address |
| `$FFFF8209` | Video counter low | Low byte of the current video fetch address |
| `$FFFF820F` | Line offset | Additional words to skip between display lines |

These are memory-mapped hardware registers, not ordinary RAM locations. Their effects are timing-dependent: the programmed base establishes the intended starting point, while the counter tracks the stream being fetched.

##### Base address versus current counter

It is useful to distinguish two concepts:

- **Video base address:** where the display is intended to start.
- **Video address counter:** the current location from which the video hardware is fetching data.

On the STE, the video counter is writable. Writing it during active display can therefore redirect subsequent fetches, which software can exploit for split screens and raster effects. Such writes can also produce unexpected output if performed at the wrong point in a scanline.

The STE supports a more flexible display base alignment than the original ST. The low-byte register allows finer placement than the original ST's 256-byte boundary restriction, although the hardware still imposes alignment constraints. 

#### 6. Horizontal scrolling and the line-offset register

Hardware scrolling is one of the STE's major video enhancements. The original ST can scroll by manipulating memory and shifting image data in software; the STE adds hardware support for fine horizontal scrolling and virtual screens.

Two important registers are:

- `$FFFF8264` — horizontal scroll (`HSCROLL`).
- `$FFFF820F` — line offset, commonly called `LINEWID` in STE documentation.

The line-offset register specifies how many **additional 16-bit words** the display address should skip after the words fetched for a scanline. This lets a program use a virtual screen wider than the visible display.

##### Example: a horizontally scrolling playfield

Imagine a game world that is 400 pixels wide, but the display shows only 320 pixels.

The game draws the world into a wider area of RAM. As the player moves, the program adjusts the video starting position and the fine-scroll value. The Shifter can then begin displaying at a pixel offset rather than requiring every scroll step to shift the entire bitmap in RAM.

<box gap={2} border radius="lg" padding={3}>
  <title size="md">Conceptual view of a scrolling line</title>
  <box background="surface-secondary" radius="md" padding={2} gap={1}>
    <text color="secondary" size="xs">Virtual screen stored in RAM</text>
    <row gap={1}>
      {#each ["A","B","C","D","E","F","G","H"] as p}
        <box key={p} flex="1" background="surface" border radius="sm" padding={2} align="center">
          <text weight="medium">{p}</text>
        </box>
      {/each}
    </row>
  </box>
  <row align="center" gap={2}>
    <icon name="arrow-down" size="lg" />
    <text color="secondary" size="sm">Video base + fine-scroll offset</text>
  </row>
  <box background="surface" border radius="md" padding={2} gap={1}>
    <text color="secondary" size="xs">Visible viewport</text>
    <row gap={1}>
      {#each ["C","D","E","F","G"] as p}
        <box key={p} flex="1" background="rgba(59,130,246,0.14)" radius="sm" padding={2} align="center">
          <text weight="medium">{p}</text>
        </box>
      {/each}
    </row>
  </box>
  <text color="secondary" size="xs">Illustrative only: actual word-fetch groups, pixel offsets and line advancement depend on resolution and register values.</text>
</box>

**The subtle part:** horizontal scrolling changes how the fetched pixel stream is aligned with the visible viewport. The line offset must be set consistently with the extra data fetched for the scroll. If the scroll offset is nonzero, the required line advance is not necessarily the same as the advance for an unscrolled display. The STE documentation specifically warns that the line-offset value must account for the extra fetch behaviour. 

##### Vertical scrolling

There is no single STE register that simply says “scroll down by 37 pixels.” Vertical scrolling is generally implemented by changing the video base address between frames, selecting different rows from a larger virtual screen, and using the line-offset mechanism when the virtual width exceeds the visible width.

For split-screen effects, software can also change the current video counter or related display registers during the frame. That is more timing-sensitive than a conventional frame-to-frame scroll.

##### A real hardware caveat

The STE has a documented fine-scroll timing quirk: certain transitions in the pixel-skip register can result in an unexpected display address. Atari-era developer notes recommend changing the relevant scroll settings at an appropriate horizontal-blanking point rather than blindly updating them in the vertical-blank handler. This matters if you aim for cycle-accurate compatibility with demos and games, not just correct-looking static images. 

#### 7. Video timing: scanlines, blanking and synchronization

A video subsystem has to generate a continuous signal even when the CPU is not executing graphics code. It must know when each scanline starts, when visible pixels begin, when the visible region ends and when the next frame begins.

The GSTMCU's video-control logic produces horizontal and vertical synchronization and display-enable timing. The Shifter emits pixel data in coordination with these signals, while the memory controller fetches data ahead of its use.

For PAL colour output, the familiar nominal timing is:

| Property | Typical PAL STE output |
|---|---:|
| Refresh rate | 50 Hz |
| Scanline rate | 15.625 kHz |
| Lines per frame | 312.5 nominal |
| Visible colour lines | 200 |
| Visible horizontal resolution | 320 or 640 pixels |

The 312.5-line figure is the nominal PAL interlaced television standard, not a claim that the STE generates a conventional interlaced broadcast picture. The STE's computer display timing is its own raster mode. For emulator work, model the actual machine's line and frame timing rather than assuming a generic television renderer.

##### What is a scanline?

A scanline is one complete horizontal sweep of the video output, including the visible pixels and the non-visible timing intervals.

A simplified scanline looks like this:

<box border radius="lg" padding={3} gap={2}>
  <row gap={1} height="36px">
    <box background="#64748b" radius="sm" flex="1" align="center" justify="center">
      <text color="#FFFFFF" size="xs">Sync</text>
    </box>
    <box background="#94a3b8" radius="sm" flex="2" align="center" justify="center">
      <text color="#111827" size="xs">Blanking</text>
    </box>
    <box background="#2563eb" radius="sm" flex="6" align="center" justify="center">
      <text color="#FFFFFF" size="sm" weight="medium">Visible pixels</text>
    </box>
    <box background="#94a3b8" radius="sm" flex="2" align="center" justify="center">
      <text color="#111827" size="xs">Blanking</text>
    </box>
  </row>
  <text color="secondary" size="xs">Conceptual only; not to scale. Actual timing and visible-area boundaries depend on the video mode and hardware timing.</text>
</box>

The display-enable signal determines when the Shifter's pixel stream is intended to be visible. During blanking, the monitor still needs the correct synchronization and blanking signals, but the active picture is not being displayed normally.

This is why changing the colour palette or video counter at a precise moment can produce raster effects. Software is modifying hardware state while the beam-equivalent display process is already progressing through the frame.

##### Why cycle accuracy matters

A frame-based emulator might render an entire image after the CPU has completed its frame's work. That can reproduce ordinary desktop graphics, but it cannot accurately represent all raster effects.

A more accurate emulator advances video state as time passes:

1. Advance the video timing counters.
2. Schedule memory fetches at the appropriate times.
3. Feed fetched words into the Shifter.
4. Apply register writes when they occur.
5. Generate pixels and synchronization from the current hardware state.

The important point is that the CPU, video logic and memory controller are separate agents that progress together. They cannot always be represented faithfully by a single “render screen now” operation. Atari ST/STE emulator implementations such as MAME's video subsystem are useful references for this separation. 

#### 8. Border effects and raster tricks

The standard visible resolutions do not describe every pixel the hardware can output. The video system also has border regions, and software can sometimes manipulate display timing and register state to alter the apparent border or create mid-screen effects.

Common techniques include:

- **Palette changes:** change a palette entry during the display to create multiple colour bands.
- **Split screens:** change the video address or scrolling state partway through a frame.
- **Border opening:** use timing-sensitive register changes to extend the apparent display area into a border region.
- **Raster bars:** change palette values at chosen scanlines.
- **Fine scrolling:** use the STE's scroll hardware to shift the displayed pixel stream without moving all bitmap data in RAM.

These effects have different levels of difficulty to emulate. Palette changes at scanline boundaries are a reasonable first target. Precise border opening and mid-scanline effects require closer attention to the GSTMCU and Shifter timing.

Do not assume every register write takes effect at the next frame boundary. Some take effect immediately or at a hardware-defined point, and the exact behaviour is part of compatibility.

#### 9. The video registers worth implementing first

This is the practical register checklist for an STE emulator.

| Address | Function | Priority |
|---|---|---|
| `$FFFF8240–$FFFF825E` | 16 palette registers | Essential |
| `$FFFF8260` | Resolution/mode control | Essential |
| `$FFFF8201`, `$FFFF8203` | Video base address | Essential |
| `$FFFF820D` | STE video-base low byte | Essential |
| `$FFFF8205`, `$FFFF8207`, `$FFFF8209` | Current video address counter | Essential |
| `$FFFF820F` | Line offset | Essential for scrolling |
| `$FFFF8264` | Horizontal scroll | Essential for STE compatibility |
| `$FFFF820A` | Video synchronization control | Essential |
| Display-enable and sync timing | Raster generation | Essential for accurate timing |

The exact read/write semantics of each register matter. In particular, the base address and current counter are not interchangeable, and the STE adds writable counter behaviour absent from the original ST. The palette and resolution registers are also part of the CPU-visible interface. 

#### 10. Video output and the French Peritel configuration

The STE generates RGB video for an external monitor through its monitor connector. A French-market setup can use an appropriate RGB-to-SCART/Peritel cable, depending on the machine and cable wiring.

For emulation, separate these layers:

- **Video hardware:** creates the pixel stream and sync timing.
- **RGB signal model:** converts the digital palette values into channel intensities.
- **Output backend:** presents those pixels on a modern display, optionally with scaling, scanlines or other visual effects.

Don't build the French Peritel cable's electrical behaviour into the Shifter emulation. The cable is an output-interface concern. PAL timing is a hardware configuration choice; SCART is the physical display connection.

If you want to reproduce analogue output characteristics, model RGB levels, blanking and sync separately from the digital pixel renderer. For an ordinary software emulator, generating a correctly timed digital image is a good initial goal.

#### 11. A recommended emulator architecture

I would build the STE video subsystem in layers, rather than trying to implement every obscure effect from day one.

<box border radius="lg" padding={3} gap={3}>
  <row align=start gap={3}>
    <box background="surface-secondary" radius="lg" size="42px" align="center" justify="center">
      <icon name="layers" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **Layer 1 — Register model**

      Implement palette, mode, base address, video counter, line offset, horizontal scroll and sync control, including correct read/write behaviour.
    </box>
  </row>
  <divider color="subtle" />
  <row align=start gap={3}>
    <box background="surface-secondary" radius="lg" size="42px" align="center" justify="center">
      <icon name="memory-stick" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **Layer 2 — Video memory fetcher**

      Fetch the correct words from shared RAM according to the current resolution and raster position. Coordinate accesses with the memory controller rather than reading the whole framebuffer in one step.
    </box>
  </row>
  <divider color="subtle" />
  <row align=start gap={3}>
    <box background="surface-secondary" radius="lg" size="42px" align="center" justify="center">
      <icon name="palette" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **Layer 3 — Shifter and palette**

      Reconstruct pixel indices from bitplanes, apply scrolling and resolve each index using the palette state effective at that time.
    </box>
  </row>
  <divider color="subtle" />
  <row align=start gap={3}>
    <box background="surface-secondary" radius="lg" size="42px" align="center" justify="center">
      <icon name="activity" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **Layer 4 — Raster timing**

      Generate scanlines, blanking, sync, frame boundaries and precisely timed register changes. This layer eventually enables accurate border and demo effects.
    </box>
  </row>
  <divider color="subtle" />
  <row align=start gap={3}>
    <box background="surface-secondary" radius="lg" size="42px" align="center" justify="center">
      <icon name="monitor" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **Layer 5 — Host display**

      Present the completed or progressively rendered image on the host system. Scaling and optional visual filters belong here, not in the hardware model.
    </box>
  </row>
</box>

##### Suggested implementation order

1. Render all three standard modes correctly.
2. Implement the 12-bit STE palette and palette register writes.
3. Implement video-base programming and the writable video counter.
4. Implement the line offset and horizontal scrolling.
5. Advance raster timing alongside CPU execution.
6. Add raster palette effects, split screens and border tricks.
7. Validate timing-sensitive software against established emulators and test programs.

The most important architectural choice is to make **video register writes and memory fetches time-aware from the beginning**. You can initially use simpler timing for ordinary display output, but if the renderer assumes that the framebuffer and palette are immutable for an entire frame, adding accurate STE raster effects later becomes much harder.

For further reading, the best starting points are the [Atari STE hardware documentation](https://www.atari-wiki.com/index.php?title=Introduction_to_the_STE), the [Atari Compendium's video-register reference](https://info-coach.fr/atari/software/_development/Atari-Compendium.pdf), and the [MAME Atari ST video implementation](https://github.com/mamedev/mame/blob/master/src/mame/atari/stvideo.cpp).

---

## Turn 24 — 2026-10-28 09:08 UTC

### Question

> document in great details the location, semantics, and timing of each ste video subsystem register

### Answer

### Atari STE video registers: location, semantics and timing

This reference targets a standard Atari STE, including the mature CA4003290 configuration with TOS 1.62 and PAL colour output. It documents the CPU-visible video registers and the timing rules that matter when emulating them.

The primary references are the maintained [Atari ST/STE memory map](https://atari-wiki.co.uk/index.php?title=IDE,_Memory_control_%26_video), the [Atari Compendium](https://info-coach.fr/atari/software/_development/Atari-Compendium.pdf), and the technical articles on [STE scrolling](https://alive.atari.org/alive12/ste_hwsc.php). Where the references disagree or an effect is not firmly established, I’ll distinguish documented behaviour from implementation details and uncertain quirks. 

#### 1. Register map at a glance

All addresses below are in the 68000's memory-mapped I/O space. For an emulator, use the full address shown, including the `FFFF` prefix.

| Address | Size | Register | Access on STE |
|---|---|---|---|
| `$FFFF8201` | Byte | Video base address, high | Read/write |
| `$FFFF8203` | Byte | Video base address, middle | Read/write |
| `$FFFF8205` | Byte | Video address counter, high | Read/write |
| `$FFFF8207` | Byte | Video address counter, middle | Read/write |
| `$FFFF8209` | Byte | Video address counter, low | Read/write |
| `$FFFF820A` | Byte | Sync mode | Read/write |
| `$FFFF820D` | Byte | Video base address, low | Read/write |
| `$FFFF820F` | Byte | Line offset (`LINEWID`) | Read/write |
| `$FFFF8240–$FFFF825E` | Word | Palette registers 0–15 | Read/write |
| `$FFFF8260` | Byte | Resolution control, GSTMCU and Shifter | Read/write |
| `$FFFF8261` | Byte | Shifter-only resolution control | Read/write |
| `$FFFF8264` | Byte | Horizontal scroll, no prefetch | Read/write |
| `$FFFF8265` | Byte | Horizontal scroll, with prefetch | Read/write |

The addresses between these registers are not necessarily additional STE video registers. In particular, `$FFFF820E` is the Falcon's word-sized line-offset register, not the STE's line-offset register. The STE uses `$FFFF820F`. Likewise, `$FFFF8262` is associated with TT video hardware and is not a standard STE mode register. 

A useful distinction before examining the registers:

- **Base address registers** specify where the next display frame should begin.
- **Video counter registers** track—or directly alter—the address from which the current display is fetched.
- **Display-control registers** configure how the video stream is interpreted and timed.
- **Palette registers** translate pixel indices into RGB intensities.
- **Scroll registers** change the alignment of the fetched pixel stream and the amount of memory consumed per line.

#### 2. Video base address registers

These registers select the beginning of the screen buffer. They are implemented by the memory-control/video circuitry rather than being ordinary Shifter palette or mode registers.

##### `$FFFF8201` — video base address, high byte

- **Width:** 8 bits.
- **Access:** read/write.
- **Address bits:** bits 23–16 of the video base address.
- **Timing:** the new base is normally applied at the next vertical blank (VBL); writes during VBL can take effect immediately.
- **Use:** sets the upper byte of the display buffer address.

##### `$FFFF8203` — video base address, middle byte

- **Width:** 8 bits.
- **Access:** read/write.
- **Address bits:** bits 15–8.
- **Timing:** same base-address update rule as `$FFFF8201`.
- **Use:** selects the middle byte of the screen address.

##### `$FFFF820D` — video base address, low byte

- **Width:** 8 bits.
- **Access:** read/write on STE; absent on the original ST.
- **Address bits:** bits 7–0, with bit 0 ignored. The base address must therefore be even.
- **Timing:** participates in the same base-address programming process.

The original ST only exposed the high and middle bytes, forcing the video base to be aligned to a 256-byte boundary. The STE adds `$FFFF820D`, allowing the display to start at any even address in accessible RAM. This enables fine vertical scrolling: software can advance the base by one scanline's worth of data, or by a smaller amount, rather than being limited to 256-byte steps. 

The full base address can be constructed as:

\[
\text{base} =
(\text{high} \ll 16)
\;|\;
(\text{middle} \ll 8)
\;|\;
(\text{low} \mathbin{\&} \texttt{0xFE})
\]

##### Programming order and update timing

When changing the base during VBL, software conventionally writes high, middle and low in that order:

```asm
move.b  new_high,$FFFF8201.w
move.b  new_mid, $FFFF8203.w
move.b  new_low, $FFFF820D.w
```

The order matters because software should avoid leaving the video system with a mixture of old and new address bytes while the screen is active. The base address is not a general-purpose pointer that is guaranteed to redirect the very next fetch on every write; the documented frame-boundary behaviour matters.

For an emulator, maintain a programmed base-address value and a separate active video counter. Don't simply change the counter immediately whenever one of the base bytes is written. 

#### 3. Video address counter registers

The counter represents the current address in the video-memory fetch stream. On the original ST, these registers were read-only. On the STE, they are writable, enabling direct changes to the fetch position during a frame.

##### `$FFFF8205` — video counter, high byte

- **Address bits:** bits 23–16.
- **Access:** read/write on STE.
- **Timing:** writing it changes the counter used for subsequent video fetches.

##### `$FFFF8207` — video counter, middle byte

- **Address bits:** bits 15–8.
- **Access:** read/write.
- **Timing:** writing it changes the current fetch position.

##### `$FFFF8209` — video counter, low byte

- **Address bits:** bits 7–0, with bit 0 treated as zero.
- **Access:** read/write.
- **Timing:** writing it changes the current fetch position.

The address is reconstructed using the same even-address rule as the base registers:

\[
\text{counter} =
(\text{high} \ll 16)
\;|\;
(\text{middle} \ll 8)
\;|\;
(\text{low} \mathbin{\&} \texttt{0xFE})
\]

##### Why the counter matters

The counter is not just a diagnostic register. It allows software to redirect video fetching without changing the next frame's base. For example, a program can use it to jump to another area of a virtual screen partway through a frame.

This is also useful for raster effects that depend on the current display position. Reading the counter gives software a view of the fetch address, but the display continues advancing as time passes.

**Emulator timing rule:** counter writes must affect subsequent fetches, not retroactively alter pixels already emitted. A write in the middle of a scanline can therefore produce a mixture of data from the previous and new addresses.

The three counter bytes are not an atomic 24-bit register. Hardware and emulator implementations may latch the counter for reads so that successive byte reads can represent one consistent snapshot, but the exact read-latching behaviour should be verified against the target implementation before relying on it for cycle accuracy. 

#### 4. `$FFFF820A` — sync mode register

This byte controls the display synchronization configuration.

| Bit | Meaning | STE interpretation |
|---|---|---|
| 1 | Vertical frequency selection | `0` = approximately 60 Hz; `1` = approximately 50 Hz |
| 0 | Sync source | `0` = internal sync; `1` = external sync |
| 7–2 | Reserved / not implemented as ordinary control bits | Keep zero for normal operation |

The conventional values are:

- `$00` — internal, 60 Hz mode.
- `$02` — internal, 50 Hz mode.

Bit 0 is relevant to unusual synchronization and border effects. Switching between internal and external synchronization at a carefully chosen time can disturb the normal border timing. Ordinary software should not use it as a general-purpose resolution or refresh control. 

##### Timing significance

This register affects the raster's synchronization behaviour, not just the host display's refresh-rate preference. A software write while the display is active can affect the timing of sync generation and therefore alter the output.

For a normal PAL STE target, the emulator should model the approximately 50 Hz raster timing. The familiar PAL colour mode has a line duration of approximately 64 microseconds and about 313 lines per frame, corresponding to roughly 49.92 Hz. These timings are nominal; the emulator should reproduce the actual STE raster rather than forcing a generic television timing model onto it. 

#### 5. `$FFFF820F` — line offset (`LINEWID`)

This register is essential to STE hardware scrolling.

- **Width:** 8 bits.
- **Access:** read/write.
- **Unit:** 16-bit words, not bytes.
- **Purpose:** specifies the number of extra words to skip when advancing between display lines.
- **Timing:** affects the address progression at line boundaries.

The key semantic detail is that this is an **extra skip**, not the total width of a scanline. A zero value means no extra words are skipped beyond the normal line-fetch progression.

Suppose the display fetches the usual amount of data for a 320-pixel low-resolution line, but the program wants to use a wider virtual screen. The line-offset value allows the video address to advance beyond the words consumed by the visible portion, positioning the next line farther along the virtual screen.

For example, a line offset of 8 means an additional 8 words, or 16 bytes, are skipped when advancing the display address.

##### Interaction with horizontal scrolling

When the fine-scroll value is nonzero, the Shifter may fetch an additional group of words at the beginning of each line. The line offset must compensate for this extra fetch so the next scanline starts at the intended location in the virtual screen.

A useful conceptual formula is:

\[
\text{next line address}
=
\text{current line start}
+
\text{normal fetched data}
+
\text{extra line offset}
\]

This is a conceptual description, not a substitute for modelling the Shifter's mode-dependent fetch sequence. In an emulator, compute the normal line fetch length from the current resolution, then apply the line offset at the appropriate line-advance point. 

##### A timing caution

Don't apply the offset after rendering an entire frame. The offset belongs to the hardware's line-by-line address progression. If the program changes it during active display, the value observed at a subsequent line boundary determines how the fetch stream advances from that point.

The exact interaction between writes, prefetch and a pending line advance should be driven by your emulated raster state.

#### 6. Palette registers: `$FFFF8240` through `$FFFF825E`

There are 16 palette registers, each 16 bits wide. Their addresses are spaced two bytes apart.

\[
\text{palette address}(n) = \texttt{\$FFFF8240} + 2n
\]

where \(n\) ranges from 0 through 15.

| Address | Register |
|---|---|
| `$FFFF8240` | Palette 0 |
| `$FFFF8242` | Palette 1 |
| `$FFFF8244` | Palette 2 |
| `$FFFF8246` | Palette 3 |
| `$FFFF8248` | Palette 4 |
| `$FFFF824A` | Palette 5 |
| `$FFFF824C` | Palette 6 |
| `$FFFF824E` | Palette 7 |
| `$FFFF8250` | Palette 8 |
| `$FFFF8252` | Palette 9 |
| `$FFFF8254` | Palette 10 |
| `$FFFF8256` | Palette 11 |
| `$FFFF8258` | Palette 12 |
| `$FFFF825A` | Palette 13 |
| `$FFFF825C` | Palette 14 |
| `$FFFF825E` | Palette 15 |

Each palette register holds one 12-bit RGB colour value. The STE has four intensity bits per channel, giving 4096 possible RGB colours. In low resolution, pixel indices select 16 palette entries; medium resolution uses four entries; high resolution is monochrome.

##### STE colour encoding

The documented logical layout is:

<box border radius="lg" padding={3} gap={2}>
  <grid columns={4} gap={1}>
    {#each ["Unused","Red","Green","Blue"] as label, i}
      <grid-item>
        <box background="surface-secondary" radius="md" padding={2} align="center" gap={1}>
          <text weight="medium" size="sm">{label}</text>
          <text color="secondary" size="xs">{i === 0 ? "4 bits" : "4 bits"}</text>
        </box>
      </grid-item>
    {/each}
  </grid>
  <text color="secondary" size="sm">Conceptual word layout: `0000 RRRR GGGG BBBB`</text>
</box>

The STE encoding preserves compatibility with the older ST's three-bit-per-channel palette: the added intensity bit is positioned so that legacy colour values retain their intended intensity. When implementing the register, follow the STE bit wiring and intensity mapping rather than assuming that every nibble behaves like a generic four-bit colour channel. 

##### Timing semantics of palette writes

Palette writes are one of the most important sources of raster effects.

- A write changes the palette state used by subsequent pixel generation.
- Pixels already emitted must retain the colour values they had when they were emitted.
- A palette change during active display can create a horizontal colour boundary or change only part of a scanline.
- Multiple writes during one frame can make the same pixel index produce different colours in different regions of the image.

This means that an emulator should not simply decode all bitplanes into pixel indices and then colourise the entire completed frame using the final palette. That approach breaks programs that change palette registers during the raster.

For a first implementation, apply writes at the next emulated video event or pixel-group boundary consistent with the rest of the renderer. For cycle-accurate compatibility, model the actual CPU write time relative to Shifter fetches and pixel emission.

##### Recommended implementation

Store the 16 palette registers as 16-bit values. Mask or decode the meaningful colour bits when calculating RGB output, but preserve the correct CPU-visible readback semantics. Then associate palette state with the raster position at which each write takes effect.

Do not treat the palette as a single 12-bit register: it is 16 separate entries, each independently programmable.

#### 7. `$FFFF8260` — resolution control for GSTMCU and Shifter

This byte controls the standard ST/STE resolution mode.

| Bits 1–0 | Mode | Pixel dimensions | Bitplanes | Palette entries |
|---|---|---|---|---|
| `00` | Low | 320 × 200 | 4 | 16 |
| `01` | Medium | 640 × 200 | 2 | 4 |
| `10` | High | 640 × 400 | 1 | Monochrome |
| `11` | Reserved | Not a standard mode | — | — |

The upper bits are not ordinary resolution controls; keep them clear in normal operation. The high-resolution mode is intended for monochrome monitor timing, whereas low and medium are the colour modes.

##### Why this register is unusual

The resolution value is needed by both the Shifter and the system's video-timing logic. The Shifter needs to know how many bitplanes to combine for each pixel; the GSTMCU needs to select the corresponding raster timing.

For example, low resolution consumes four bitplanes per pixel. Medium resolution consumes two. High resolution consumes one bitplane and uses a different horizontal and vertical timing configuration.

A write to `$FFFF8260` therefore cannot be treated as merely changing a software variable used by the renderer. It changes the way video data is interpreted and the way the raster is generated.

##### Timing semantics

A resolution write during active display can alter the pixel stream and potentially the sync timing. Such writes are used by unusual programs and demos, but they are not a safe way to change the normal display mode mid-frame.

For ordinary software, mode changes are usually performed during initialization or at a frame boundary. An emulator should nevertheless preserve writes as timed events so that mid-frame effects are not silently converted into frame-boundary updates. 

#### 8. `$FFFF8261` — Shifter-only resolution register

This register is less widely known, but it matters for hardware-accurate emulation.

- **Width:** byte.
- **Access:** read/write on ST/STE.
- **Function:** changes the resolution state in the Shifter without changing the GSTMCU's separate resolution state.
- **Readback:** returns the Shifter's resolution value.

On the STE, `$FFFF8260` updates the mode in both the Shifter and GSTMCU. `$FFFF8261` updates only the Shifter. This permits software to put the two components into different states—a technique used by some demos and low-level effects.

The Shifter's state controls how incoming video words are decoded; the GSTMCU's state controls associated display timing. If the states disagree, the resulting display may be unusual, and the behaviour is not equivalent to setting a normal resolution mode.

For a conventional emulator, implement `$FFFF8261` as a separate Shifter-side mode value. Do not alias it to `$FFFF8260` unless you intentionally choose a simplified compatibility mode. 

#### 9. `$FFFF8264` and `$FFFF8265` — horizontal scroll

The STE has two closely related horizontal-scroll registers.

| Address | Meaning |
|---|---|
| `$FFFF8264` | Horizontal scroll without prefetch |
| `$FFFF8265` | Horizontal scroll with prefetch; the documented STE scrolling register |

Both use the low four bits to represent a pixel-scroll value from 0 to 15. The upper four bits are not part of the scroll value.

##### `$FFFF8265`: scroll with prefetch

This is the conventional STE fine-scroll register. When its scroll value is nonzero, the Shifter fetches an additional group of words at the start of each line, allowing the visible pixel stream to be shifted by a sub-word amount.

The extra fetch changes how many words are consumed per line, so `$FFFF820F` must be configured consistently to maintain the intended virtual-screen width.

##### `$FFFF8264`: scroll without prefetch

This register applies a similar pixel offset without the same extra-prefetch behaviour. It is not as well documented in Atari's published programmer references, but is implemented in established emulator code and has been used by demos.

For a robust emulator, implement the two registers separately. Even if normal desktop software only uses `$FFFF8265`, some software can distinguish them. 

##### Timing and the well-known scrolling quirk

The scroll value interacts with line-fetch state, so a change can affect not only pixel alignment but also subsequent address progression.

Atari-era developer notes report a corruption issue when the prefetch scroll register is changed from a nonzero value to zero during VBL. The recommended workaround is to manage the change carefully, sometimes using an HBL-timed update. Some routines also reload the video counter after changing the base, scroll and line-offset registers.

This is a hardware-compatibility concern, not a rule that every scroll write must trigger corruption. The exact effect depends on the sequence and timing of writes. 

#### 10. Register timing summary

This table distinguishes the *intended update point* from the *emulator event that should observe the change*. They are not necessarily the same thing.

| Register group | When the change matters | Recommended emulation behaviour |
|---|---|---|
| Base address `$8201/$8203/$820D` | Normally next VBL; special case for writes during VBL | Maintain programmed base separately from active counter |
| Counter `$8205/$8207/$8209` | Subsequent fetches after the write | Update the fetch pointer; don't redraw emitted pixels |
| Sync mode `$820A` | Raster synchronization timing | Apply the changed sync configuration at its hardware-effective time |
| Line offset `$820F` | Line-to-line address progression | Use the value effective at the line-advance event |
| Palette `$8240–$825E` | Subsequent pixel output | Preserve earlier pixel colours; apply timed palette changes |
| Resolution `$8260/$8261` | Pixel decoding and possibly display timing | Keep GSTMCU and Shifter mode state distinct |
| Scroll `$8264/$8265` | Pixel alignment, fetch count and line progression | Model the prefetch and non-prefetch variants separately |

The important caveat is that the register documentation does not specify an exact CPU-cycle latency for every write. Some behaviours are well established—such as the next-VBL base update and writable STE video counter—while others require tracing the actual hardware fetch pipeline. Do not assign an arbitrary universal latency to every video register.

#### 11. CPU access semantics

The video registers reside in the 68000's memory-mapped I/O space. The 68000 normally accesses the Atari's hardware I/O in supervisor mode; ordinary user-mode access can cause a bus error.

For the emulator, pay attention to these distinctions:

- **Byte registers:** handle byte reads and writes at their specified addresses.
- **Palette registers:** use word-aligned, 16-bit register semantics. Don't silently treat adjacent palette registers as one 32-bit value.
- **Read-only versus writable:** the video counter is read-only on an original ST but writable on the STE.
- **Reserved bits:** do not let arbitrary upper bits change resolution or scroll state unless hardware evidence supports that behaviour.
- **Readback:** register reads should reflect the hardware-visible state, which may differ from a software-maintained shadow variable.

The 68000 has separate upper and lower data-byte strobes, so byte accesses to memory-mapped registers must be decoded correctly. This is particularly important for `$FFFF8260` and `$FFFF8261`, which represent distinct mode-control paths. 

#### 12. A practical implementation model

A good STE video implementation maintains these pieces of state independently:

<box border radius="lg" padding={3} gap={3}>
  <row align="start" gap={2}>
    <icon name="database" size="lg" />
    <box flex="1" gap={1}>
      **Programmed registers**

      The values last written by the CPU: base address, line offset, scroll values, resolution, sync mode and palette.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={2}>
    <icon name="move-horizontal" size="lg" />
    <box flex="1" gap={1}>
      **Live video state**

      The active fetch address, current scanline and horizontal position, current display-enable state, and data already loaded into the Shifter.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={2}>
    <icon name="clock" size="lg" />
    <box flex="1" gap={1}>
      **Scheduled events**

      Video fetches, line boundaries, VBL transitions, palette updates and mode changes, all coordinated with CPU execution.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={2}>
    <icon name="monitor" size="lg" />
    <box flex="1" gap={1}>
      **Output pixels**

      The RGB values already generated. Later palette or address changes must not retroactively change them.
    </box>
  </row>
</box>

A CPU write should update the programmed register and schedule or apply its effect according to that register's semantics. The video engine then uses the live state when the relevant fetch, line boundary or pixel-generation event occurs.

This design also avoids a common emulator bug: applying all register writes only at the beginning of the next frame. That is sufficient for simple desktop applications but breaks raster effects, palette splits and some scrolling techniques.

#### 13. Recommended validation order

I would implement and test the registers in this order:

1. **Base address and counter:** verify correct screen placement, vertical scrolling and mid-frame counter changes.
2. **Resolution and palette:** verify all three modes and STE RGB intensity mapping.
3. **Line offset and `$FFFF8265`:** verify virtual-width screens and fine horizontal scrolling.
4. **`$FFFF8264`:** implement and test the no-prefetch behaviour separately.
5. **Sync mode and VBL timing:** verify PAL frame timing and normal mode transitions.
6. **`$FFFF8261`:** test deliberate disagreement between the Shifter and GSTMCU resolution state.
7. **Raster accuracy:** test mid-scanline palette writes, counter writes, scroll changes and border effects.

For reference, the maintained [Atari ST/STE memory map](https://atari-wiki.co.uk/index.php?title=IDE,_Memory_control_%26_video) is particularly useful because it identifies known disagreements and cross-checks the register semantics against Hatari, EmuTOS and Atari's own TOS sources. The [Atari Compendium](https://info-coach.fr/atari/software/_development/Atari-Compendium.pdf) is a useful complementary programmer's reference, while the [STE scrolling article](https://alive.atari.org/alive12/ste_hwsc.php) provides practical examples of address, offset and pixel-skip interactions. 

**The central rule for an accurate emulator:** model the video registers as controls over an ongoing fetch-and-display process, not as static parameters for drawing a completed framebuffer. That is the foundation for getting the ordinary display correct and for supporting the more demanding raster effects later.

---

## Turn 25 — 2026-10-28 09:50 UTC

### Question

> document in great details the shifter and gstmcu input and output signals and the state machines / algorithms in between that drive the video output signals

### Answer

### Atari STE video generation: signals, state machines and algorithms

The most useful way to understand the STE's video hardware is to follow the electrical signals from the timing generator, through memory fetches and the Shifter, to the RGB output pins.

A crucial distinction: the **GSTMCU is not merely a replacement for the original ST's GLUE chip**. It combines important GLUE and MMU functions. Its timing logic determines *when* the display is active and when memory must supply video data; its memory-control logic helps arrange the RAM accesses; and the separate GST Shifter converts fetched data into a serial pixel stream. Atari's service manual describes the GSTMCU's `DE`, `BLANK`, `HSYNC` and `VSYNC` signals and the memory controller's `DCYC` signal as parts of this coordinated process. 

For emulator development, I recommend separating the system into four interacting machines:

1. **Horizontal timing machine** — advances through each scanline.
2. **Vertical timing machine** — advances through lines and frames.
3. **Video memory-fetch and Shifter machine** — fetches words and serializes bitplanes.
4. **Output gating machine** — determines whether the resulting pixel values reach the RGB outputs or are suppressed during blanking.

The first two are primarily GSTMCU responsibilities; the third spans memory control and the Shifter; the fourth is shared between the Shifter and the display-control signals.

#### 1. System-level signal path

<box border radius="xl" padding={3} gap={2} align="center">
  <box border radius="lg" padding={3} width="100%" align="center" gap={1}>
    <title size="md">GSTMCU</title>
    <text color="secondary" textAlign="center" size="sm">Horizontal/vertical timing, memory address control, DRAM scheduling</text>
  </box>
  <grid columns={2} gap={2}>
    <grid-item>
      <box background="surface-secondary" radius="md" padding={2} align="center" gap={1}>
        <text weight="medium">Timing outputs</text>
        <text size="sm" textAlign="center">DE, BLANK, HSYNC, VSYNC</text>
      </box>
    </grid-item>
    <grid-item>
      <box background="surface-secondary" radius="md" padding={2} align="center" gap={1}>
        <text weight="medium">Fetch control</text>
        <text size="sm" textAlign="center">DCYC, LOAD, address progression</text>
      </box>
    </grid-item>
  </grid>
  <icon name="arrow-down" size="xl" />
  <box border radius="lg" padding={3} width="100%" align="center" gap={1}>
    <title size="md">GST Shifter</title>
    <text color="secondary" textAlign="center" size="sm">Input latches, FIFO/word buffers, shift registers, palette lookup, output selection</text>
  </box>
  <icon name="arrow-down" size="xl" />
  <grid columns={3} gap={2}>
    <grid-item>
      <box background="surface-secondary" radius="md" padding={2} align="center">
        <text weight="medium">Red</text>
        <text size="xs" color="secondary">Analogue</text>
      </box>
    </grid-item>
    <grid-item>
      <box background="surface-secondary" radius="md" padding={2} align="center">
        <text weight="medium">Green</text>
        <text size="xs" color="secondary">Analogue</text>
      </box>
    </grid-item>
    <grid-item>
      <box background="surface-secondary" radius="md" padding={2} align="center">
        <text weight="medium">Blue</text>
        <text size="xs" color="secondary">Analogue</text>
      </box>
    </grid-item>
  </grid>
  <text color="secondary" size="xs">HSYNC, VSYNC and blanking also feed the monitor-output circuitry.</text>
</box>

The diagram is functional rather than a literal pin-to-pin schematic. The real design has feedback and parallel paths: the GSTMCU and Shifter exchange control signals, while the GSTMCU also coordinates the memory controller and RAM address/data paths. The service manual documents the essential control relationships; the reverse-engineered [GSTMCU and Shifter Verilog model](https://github.com/gyurco/gstmcu) is useful for investigating gate-level details. 

#### 2. GSTMCU input signals

The GSTMCU receives several classes of signals relevant to video. Exact pin numbers should be taken from the corrected schematic for the board being emulated; the following is a functional signal inventory.

| Input | Function | Why it matters |
|---|---|---|
| 16 MHz clock | Main timing reference from the GST Shifter | Drives the GSTMCU's system timing and clock division |
| Reset | Resets internal counters and control state | Establishes a known initial state |
| `$FFFF8260` resolution write | Programs the GSTMCU-side display mode | Selects the timing configuration for low, medium or high resolution |
| `$FFFF820A` sync-control write | Programs the frequency/sync selection | Selects the normal 50/60 Hz configuration or special synchronization behaviour |
| CPU address, data and control bus | Carries video-register writes and reads | Allows software to alter display state |
| RAM status and data/address feedback | Reflects the current memory transaction | Allows the memory controller to coordinate display fetches |
| External synchronization, where fitted | Reference timing for genlock-related configurations | Can be relevant to special synchronized video setups |

The CPU-visible register address does not necessarily correspond to a single physical register inside one chip. The mode register at `$FFFF8260`, for example, is decoded so that the Shifter and GSTMCU can each receive their respective mode state. 

##### Clock domains

A typical STE has a 32 MHz master video clock. The GST Shifter uses that reference to generate a 16 MHz clock for the GSTMCU; the GSTMCU then derives the 8 MHz CPU/bus clock and other system clocks.

The Shifter also receives a separate 8 MHz clock called `SCLK`, used for STE DMA audio timing. This is important when modelling the full chip, although the sound clock should not be mistaken for the principal video pixel clock. 

#### 3. GSTMCU output signals

##### Core video signals

| Signal | Direction | Meaning |
|---|---|---|
| `DE` — Display Enable | GSTMCU → Shifter, memory control, and MFP Timer B | Indicates the active display interval |
| `BLANK` | GSTMCU → video output path | Suppresses normal visible output outside the permitted display region |
| `HSYNC` | GSTMCU → monitor interface | Marks horizontal synchronization |
| `VSYNC` | GSTMCU → monitor interface | Marks vertical synchronization |
| `DCYC` — Data Cycle | Memory-control side → Shifter | Indicates the video RAM data-transfer timing |
| `LOAD` / `*LOAD` | Memory-control side → Shifter | Causes video data to be loaded into the Shifter's input buffer |
| Video address/counter control | Internal GSTMCU/memory-controller function | Advances the address from which display words are fetched |

The slash or asterisk notation indicates an active-low signal where applicable. `DE` is active high in the service description; do not assume all control signals use the same polarity.

`DCYC` and `LOAD` should not be treated as interchangeable. `DCYC` is associated with the RAM data-transfer cycle; `LOAD` is the Shifter-facing load control. Their exact edge relationship depends on the hardware timing sequence. The service manual describes `DCYC` as the strobe that transfers display-memory data into the Shifter. 

##### Other GSTMCU outputs that affect video indirectly

- **Clock outputs:** provide system clocks to the CPU, MFP, ACIAs and other devices.
- **Interrupt-priority outputs:** communicate the interrupt level to the 68000. Video-related interrupts include horizontal and vertical blank interrupts.
- **Memory address and control signals:** select RAM rows and columns and coordinate data access.
- **Bus-arbitration signals:** arrange access to the shared system bus when DMA or the Blitter needs it.

These aren't video pixels, but they influence whether the CPU can access RAM at a particular moment and how reliably an emulator reproduces timing-sensitive effects.

---

#### 4. The GSTMCU's horizontal timing state machine

A useful reverse-engineered model has a horizontal machine that tracks position through each line. Its internal state determines when the line's visible region begins and ends, when blanking is asserted, and when horizontal sync is generated.

For emulation, think of the horizontal state as containing:

- A horizontal counter, `h`.
- A selected timing mode: PAL colour, NTSC colour or monochrome.
- An active-display flag, `H`.
- A blanking flag.
- An HSYNC flag.
- A line-length or end-of-line value.
- A preload flag used by the STE scrolling path.

The real GSTMCU is a custom ASIC; this is a functional state model, not a claim that it contains those exact named registers. The published [ST/STE scanline analysis](https://temlib.org/AtariForumWiki/index.php/ST_STE_Scanlines) explicitly describes its pseudocode as an incomplete model of observable behaviour rather than a full physical implementation. 

##### Typical PAL colour timing

The reverse-engineered STE timing model uses a 512-count PAL line in its timing units. If each unit is 125 ns, the line is 64 μs.

| Approximate horizontal count | Action in the model |
|---:|---|
| 0 | Start of line; evaluate mode-dependent preload conditions |
| 28 | Release blanking in 50 Hz mode |
| 40 | Trigger preload in 50 Hz mode |
| 56 | Establish the normal line-length parameter |
| 376 | End the horizontal active-display flag |
| 448 | Assert blanking |
| `LINE − 52` | Assert HSYNC and clear the horizontal active flag |
| `LINE − 12` | Deassert HSYNC |

The model uses different offsets in other timing modes. For example, the NTSC colour line is modelled with a 508-count line rather than 512, and monochrome has different horizontal timing.

These numbers are **reverse-engineered timing-model coordinates**, not CPU instruction counts and not a guarantee that every output pin changes on precisely the same internal edge. They are valuable starting points for a raster simulator, but should be validated against the Verilog model or waveform captures before being treated as exact gate-level timing. 

##### Why horizontal timing is more than a counter

If the machine merely counted to 512 and then reset, it could produce a stable line but not all the behaviours used by STE demos.

The timing logic also has to:

1. Determine when the current resolution permits active display.
2. Schedule the initial fetch sequence.
3. End active display at the appropriate point.
4. Assert and deassert horizontal synchronization.
5. Account for the hardware-scroll prefetch sequence.
6. Advance to the next line while preserving the correct vertical state.

The state transitions have latency. A mode-register write is sampled by the timing logic; the resulting change to DE or HSYNC does not necessarily occur at the exact CPU bus-write edge. This is one reason to schedule video-register writes as timed events.

#### 5. The vertical timing state machine

The vertical machine counts scanlines and controls the vertical active-display interval, vertical blanking and VSYNC.

Its conceptual state is:

- A line counter, `v`.
- A vertical active flag, `V`.
- A vertical-blank flag.
- A VSYNC flag.
- The selected refresh mode.
- A line-boundary event from the horizontal machine.

At each horizontal line boundary, the vertical counter advances. When it reaches a mode-dependent transition, the state machine changes the vertical display or synchronization state.

The key signals combine conceptually as follows:

\[
DE = H \land V
\]

Here, \(H\) and \(V\) are the horizontal and vertical active-display flags. This equation is a useful logical model of active display, although the physical signal path includes implementation-specific delays.

The GSTMCU uses the resulting display interval to coordinate the Shifter and memory controller. VSYNC also tells the memory controller when to reload the display start address during vertical blanking. 

##### What happens at VBL?

The vertical blank interval is when the raster is outside the normal visible display area. It is also the conventional time for software to prepare the next frame.

The CPU may use VBL to:

- Change the video base address.
- Update the palette for the next frame.
- Prepare a new screen buffer.
- Change resolution or synchronization configuration.
- Synchronize game logic with the display.

The hardware does not wait for the CPU to finish these operations. The GSTMCU continues generating the raster. If software writes registers too late or at an unexpected point, the change can affect the current frame rather than only the next one.

---

#### 6. The Shifter's input signals

The GST Shifter has two distinct data paths: one for the system's CPU data bus and one for video RAM data. It also has its own control-register interface. The RAM-facing path is especially important because video fetches do not require the 68000 to read each word manually. 

| Input | Purpose |
|---|---|
| RAM data bus | Supplies the words fetched from video memory |
| CPU data bus | Carries palette, resolution and other Shifter register accesses |
| Register select / chip-select signals | Select the palette or control register being accessed |
| Read/write control | Distinguishes register reads from writes |
| `LOAD` / `*LOAD` | Tells the Shifter to accept a new video word |
| `DCYC` | Synchronizes data loading with the memory cycle |
| `DE` | Indicates the active display interval |
| `BLANK` | Controls whether video output is visible |
| Resolution state | Selects how many bitplanes are combined for each pixel |
| Horizontal scroll state | Controls pixel alignment and the STE's prefetch behaviour |
| Clock inputs | Establish the timing for word loading, shifting and output |

The exact pin-level interface is wider than this list: the GST Shifter is an 84-pin device with separate signals for audio DMA, clocks and other functions. This table covers the video data path, not every pin on the package. 

#### 7. The Shifter's internal video pipeline

The Shifter's job is to turn a stream of 16-bit words into a stream of pixel colours. A useful reverse-engineered model describes four input-buffer words and four associated shift-register words. This model explains many observable behaviours, but should not be mistaken for a fully verified schematic of every internal latch. 

##### Stage A: capture a word

When the memory controller presents a word and asserts the appropriate load control, the Shifter captures the data into its input buffer.

The CPU doesn't need to copy the word through a CPU register. The video memory path is designed to load the Shifter directly.

##### Stage B: accumulate the required planes

The number of words needed for a group of pixels depends on resolution:

| Mode | Plane words for 16 pixels | Pixel indices produced |
|---|---:|---|
| Low | 4 | 16 four-bit indices |
| Medium | 2 | 16 two-bit indices |
| High | 1 | 16 one-bit values |

In low resolution, the Shifter combines corresponding bit positions from four plane words. Each group of four bits selects one of 16 palette entries.

In medium resolution, it combines two plane words to form a two-bit index. In high resolution, the single plane directly supplies the monochrome pixel state.

##### Stage C: transfer to shift registers

Once enough input words have accumulated, the Shifter transfers the group to the shift-register stage.

The shift registers are consumed as pixels are emitted. Meanwhile, the input buffers can receive more data. This overlap between loading and shifting is what lets the Shifter maintain a continuous display stream.

##### Stage D: shift and decode

At each pixel-generation event, the Shifter selects the next bit from each active plane, constructs the pixel index and uses it to select a palette entry.

Conceptually:

\[
i = b_0 + 2b_1 + 4b_2 + 8b_3
\]

for low resolution, where \(b_0\) through \(b_3\) are the current bits from the four planes.

The corresponding palette entry is converted to red, green and blue intensity values. High resolution instead selects monochrome output and bypasses normal colour-palette lookup.

##### Stage E: output gating

Even if the Shifter has a valid pixel value, that pixel is not necessarily sent to the monitor. The output is gated by the active-display and blanking controls.

When the display is blanked, the RGB outputs are suppressed or forced to the appropriate blank level. When the display is active, the selected pixel colour is driven to the output circuitry.

This is a critical distinction for border effects: **the border is not simply extra pixels in the framebuffer**. It is the output of the hardware when the normal active pixel stream is not being displayed.

---

#### 8. Horizontal scrolling changes the fetch algorithm

The STE's horizontal scrolling support is why its horizontal timing and Shifter logic differ from the original ST.

For ordinary unscrolled output, the hardware fetches the normal set of plane words needed for the line. With prefetch scrolling enabled and a nonzero scroll value, the Shifter may need an extra group of data so the visible stream can begin at an offset within the fetched words.

A simplified fetch procedure is:

```text
At the start of a scanline:
    determine resolution and scroll mode
    initialise the line's fetch state

    if scrolling requires prefetch:
        load the extra initial words

    while display data is required:
        request the next video-memory transfer
        capture the returned word
        transfer complete groups to the shift registers
        emit pixels from the shift registers

    at line end:
        apply the programmed line offset
        advance to the next scanline
```

This is pseudocode for an emulator, not a literal transcription of Atari's logic.

The reverse-engineered scanline analysis calls out a `PRELOAD` state for the STE. In its model, low resolution needs four plane words during preload, whereas high resolution needs one. The extra initial load changes the relationship between DE and the first emitted pixel. 

For an emulator, the key consequence is that scrolling cannot be modelled as simply moving the finished bitmap left by a few pixels. The scroll value changes how the fetched word stream is aligned and can alter how much memory is consumed per line.

#### 9. The timing relationship between DE, LOAD and pixel output

A particularly important detail is that **DE and the first visible pixel do not necessarily transition simultaneously**.

The reverse-engineered timing model describes the following example for the STE:

- The GSTMCU raises DE at a horizontal timing coordinate.
- The memory controller reacts later and asserts LOAD.
- The Shifter needs additional time to load and shift the words before the first corresponding RGB values appear.
- When DE falls, the memory controller stops issuing new video loads after its own delay, while already-loaded data and the output pipeline can have their own state.

The scanline analysis gives illustrative examples in which a DE transition is followed by LOAD four timing units later, and notes an additional Shifter delay before the first RGB values. Those figures are useful for understanding the pipeline, but the article explicitly labels the model incomplete. They should not be treated as a complete pin-level timing specification for all modes. 

For cycle-accurate emulation, define events separately:

1. GSTMCU changes DE.
2. Memory controller observes the change.
3. Memory address and DRAM control select the next word.
4. Data becomes valid on the RAM-facing bus.
5. The Shifter captures the word.
6. The shift-register stage consumes data.
7. The output stage emits RGB values.
8. Blanking gates the output independently.

This prevents a common modelling error: assuming that every stage changes at the exact instant of the CPU register write or the DE transition.

#### 10. RGB and monochrome output signals

The Shifter generates the colour information, but the motherboard output circuitry is responsible for delivering suitable electrical signals to the monitor connector.

The important output classes are:

| Output | Meaning |
|---|---|
| Red | Analogue intensity for the red channel |
| Green | Analogue intensity for the green channel |
| Blue | Analogue intensity for the blue channel |
| Monochrome video | Dedicated monochrome output path |
| Colour-clock-related output | Used by RF/composite encoder circuitry on applicable variants |
| HSYNC / VSYNC | Generated by the GSTMCU and routed to the monitor interface |
| Blanking | Controls when normal video output is visible |

The STE palette provides four bits of intensity per RGB channel. The digital colour value is translated into the corresponding analogue output level through the Shifter and motherboard circuitry. In monochrome mode, the normal palette path is bypassed and the dedicated monochrome output is used. 

The exact analogue voltage levels, termination and connector routing are properties of the board's output circuitry, not of the logical pixel generator. For a software emulator, the first goal should be correct RGB values and correct sync/blanking timing. Analogue electrical modelling is a separate optional layer.

---

#### 11. A combined algorithm for an emulator

The following is a useful high-level model for integrating the machines. It intentionally describes externally observable behaviour, rather than claiming to reproduce the GSTMCU's internal gate-level logic.

```text
initialise GSTMCU timing state
initialise Shifter input buffers and shift registers
initialise video base and active video counter
initialise palette, resolution, scroll and sync state

for each video timing event:
    process CPU register writes scheduled at this time

    advance horizontal timing state

    if end of scanline:
        advance vertical timing state
        update VSYNC / VBL state as required
        apply line-offset address progression

    derive active-display and blanking state
    derive horizontal and vertical sync output state

    if a video-memory fetch is due:
        compute the RAM address for the fetch
        coordinate the memory transaction
        capture the returned word in the Shifter buffer
        advance the video counter

    if the Shifter has a pixel-generation event:
        select the next bit from each active plane
        construct the pixel index
        resolve the palette colour, or monochrome value
        apply the output blanking state
        emit the RGB or monochrome output
```

A real implementation needs to preserve the ordering of events within each timing interval. If a palette write and a pixel output happen in the same nominal interval, the hardware's actual ordering determines which palette value the pixel uses. Likewise, a counter write must affect future fetches without retroactively changing already-emitted pixels.

##### Suggested implementation phases

- **Phase 1:** standard 50 Hz PAL raster, normal DE/HSYNC/VSYNC, and correct output gating.
- **Phase 2:** normal low, medium and high resolution with the correct word-fetch sequence.
- **Phase 3:** accurate video counter, line offset and fine-scroll prefetch.
- **Phase 4:** timed palette writes and mid-frame mode changes.
- **Phase 5:** border effects, sync scrolling, and unusual Shifter states.
- **Phase 6:** exact cycle-level alignment validated against test waveforms or a reference hardware implementation.

#### 12. Best technical references for going deeper

For this level of work, these sources complement each other:

- [Atari STE field service manual](https://manualzz.com/doc/66924193/atari-1040ste--520ste-field-service-manual) — official functional descriptions of the GSTMCU, video Shifter, memory controller and display signals.
- [Atari STE hardware description](https://info-coach.fr/atari/hardware/STE-HW.php) — signal relationships, component responsibilities, clock distribution and Shifter behaviour.
- [ST/STE scanline analysis](https://temlib.org/AtariForumWiki/index.php/ST_STE_Scanlines) — reverse-engineered horizontal and vertical state models, preload behaviour and timing caveats.
- [GSTMCU and GST Shifter Verilog model](https://github.com/gyurco/gstmcu) — the most directly useful of these for emulator/FPGA development because it includes asynchronous gate-level and synchronous implementations, plus testbench waveforms. 

The Verilog repository is particularly valuable because it moves beyond a behavioural description into an executable hardware model. I would use the service manual to understand the purpose of each signal, the scanline analysis to understand software-visible timing, and the Verilog implementation to resolve ordering and edge cases where the prose references are incomplete.

---

## Turn 26 — 2026-10-28 10:11 UTC

### Question

> does the atari ste have an internal speaker, what are the sound output signals, explain in great details how the sound is generated

### Answer

### Atari STE sound hardware: speakers, signals, and sound generation

**The Atari STE does not normally have a built-in internal loudspeaker.** It generates sound electronically and sends it to external audio equipment, such as amplified speakers, headphones through a suitable amplifier, or a monitor/television with audio input. The STE has dedicated audio output connectors, including separate left and right outputs, and can also provide a mono audio signal through its monitor connection. 

For emulator development, the most important fact is that the STE has **two sound-generation systems**:

- **YM2149 PSG:** the original ST's programmable sound generator, used for synthesized tones, noise, music, and sound effects.
- **8-bit stereo DMA sound:** the STE's additional system for playing digital samples directly from RAM at fixed sample rates.

Their outputs are processed by the STE's audio circuitry and its LMC1992 volume-and-tone controller before reaching the external outputs. This is not simply a single sound chip connected directly to a speaker. 

#### 1. The overall audio signal path

<box border radius="xl" padding={3} gap={2}>
  <box background="surface-secondary" radius="lg" padding={3} align="center" gap={1}>
    **YM2149 PSG**
    <text size="sm" textAlign="center">3 tone channels + noise + envelope</text>
  </box>
  <box align="center"><icon name="arrow-down-up" size="lg" /></box>
  <box background="surface" border radius="lg" padding={3} align="center" gap={1}>
    **Audio source selection and mixing**
    <text size="sm" textAlign="center">Selects/mixes PSG and DMA audio</text>
  </box>
  <box align="center"><icon name="arrow-down" size="lg" /></box>
  <box background="surface-secondary" radius="lg" padding={3} align="center" gap={1}>
    **LMC1992 audio processor**
    <text size="sm" textAlign="center">Master volume, left/right balance, bass, treble</text>
  </box>
  <box align="center"><icon name="arrow-down" size="lg" /></box>
  <grid columns={2} gap={2}>
    <grid-item>
      <box border radius="lg" padding={3} align="center" gap={1}>
        **Left audio**
        <text size="sm" textAlign="center">Analog output</text>
      </box>
    </grid-item>
    <grid-item>
      <box border radius="lg" padding={3} align="center" gap={1}>
        **Right audio**
        <text size="sm" textAlign="center">Analog output</text>
      </box>
    </grid-item>
  </grid>
  <text size="xs" color="secondary">Simplified functional diagram. The real circuit includes separate DACs, filtering, and analog stages; the PSG and DMA paths do not share an identical signal path.</text>
</box>

The PSG is inherited from the earlier ST. The DMA audio circuitry adds digital-to-analog conversion and sample-rate-dependent filtering, while the LMC1992 gives the STE software-controlled stereo volume and tone controls. The monitor's audio signal is a separate, mono-compatible output path rather than the same thing as the stereo RCA outputs. 

#### 2. What are the STE's sound output signals?

There are two ways to think about the signals: **signals visible between chips on the motherboard**, and **the final analog signals exposed outside the computer**.

##### External audio outputs

| Output | Signal type | Purpose |
|---|---|---|
| Left RCA/phono output | Analog audio | Left channel of stereo sound |
| Right RCA/phono output | Analog audio | Right channel of stereo sound |
| Monitor connector audio pin | Analog, mono-compatible audio | Sound for a compatible monitor or television |
| DMA-active signal | Internal digital logic signal | Indicates DMA sample playback activity; can feed MFP Timer A |

The RCA outputs are line-level audio outputs, not speaker-power outputs. Connect them to powered speakers, an amplifier, or appropriate line/headphone equipment—not directly to a passive loudspeaker expecting a power amplifier. The DMA-active signal is a hardware status/timing signal, not an audio waveform. 

##### Internal signals and buses

| Signal or interface | Direction / role | Meaning |
|---|---|---|
| YM2149 channel outputs | PSG → audio circuitry | Three synthesized channel signals |
| DMA sample data | RAM → DMA/Shifter sound path | Digital sample bytes fetched by DMA |
| Left/right DAC outputs | DACs → analog filter stages | Analog voltages representing sample values |
| DMA-active | DMA sound logic → MFP Timer A input | Playback activity indication, useful for event counting |
| PSG register interface | CPU ↔ YM2149 | CPU selects and reads/writes PSG registers |
| Microwire control | CPU → audio processor | Programs the LMC1992's volume, balance, tone and source-mix settings |
| Audio output stages | Audio processor/circuit → connectors | Conditioned analog audio |

The precise internal net names and pin polarities depend on the relevant schematic revision. For an emulator, most of these board-level electrical details can be represented as logical signals and audio-processing stages; you generally do not need to simulate individual op-amp voltages unless you are building a circuit-level model. 

#### 3. The YM2149: how synthesized sound is generated

The <Entity value="YM2149" category="product" disambig="Yamaha programmable sound generator chip"/> is a programmable sound generator (PSG). Unlike sample playback, it does not need a waveform stored in RAM. The chip generates tone and noise waveforms internally, using register values supplied by the 68000 CPU. 

##### 3.1 Its three tone channels

The PSG contains three independently programmable tone generators, conventionally named **A, B and C**. Each produces a square-wave-like signal.

For each channel, software sets a 12-bit tone period using two registers:

| Channel | Period low byte | Period high 4 bits |
|---|---:|---:|
| A | `$FFFF8800` selects register 0; data at `$FFFF8802` | Register 1 |
| B | Register 2 | Register 3 |
| C | Register 4 | Register 5 |

The period controls the output frequency: a smaller period gives a higher pitch, and a larger period gives a lower pitch. With a nominal 2 MHz PSG clock, a useful approximation is:

\[
f_{\text{tone}} \approx \frac{2{,}000{,}000}{16P}
\]

where \(P\) is the nonzero 12-bit tone period. The PSG's internal divider structure determines this relationship; an emulator should use the chip's actual counter behavior rather than generating arbitrary sine waves.

For example, with a period of 125:

\[
f \approx \frac{2{,}000{,}000}{16\times125}
= 1000\ \text{Hz}
\]

That produces a tone near 1 kHz. The waveform is rich in harmonics, which is why the PSG has its characteristic buzzy sound.

##### 3.2 Noise generation

The PSG also has a noise generator, controlled by register 6. It produces a pseudo-random digital noise pattern rather than a pitched square wave.

Register 7 controls whether tone and noise are enabled for each channel. This means each of A, B and C can produce:

- tone only;
- noise only;
- a mixture of tone and noise;
- neither tone nor noise.

That combination is useful for effects such as explosions, wind, percussion and rough electronic textures.

##### 3.3 Amplitude and envelope

Registers 8, 9 and 10 set the volume of channels A, B and C.

Each channel can use a fixed volume level or the shared hardware envelope generator. The envelope generator is controlled by registers 11 and 12 for its period and register 13 for its shape.

The envelope can rise, fall, repeat, or hold at a final level, depending on its programmed shape. This lets the PSG create a changing sound without the CPU having to update the volume every few milliseconds.

A simplified PSG pipeline looks like this:

<box border radius="xl" padding={3} gap={2} align="center">
  <box background="surface-secondary" radius="lg" padding={3} width="100%" align="center">
    **Tone counters + noise generator**
  </box>
  <icon name="arrow-down" size="lg" />
  <box border radius="lg" padding={3} width="100%" align="center">
    **Per-channel tone/noise mixer**
  </box>
  <icon name="arrow-down" size="lg" />
  <box border radius="lg" padding={3} width="100%" align="center">
    **Volume selection / envelope**
  </box>
  <icon name="arrow-down" size="lg" />
  <box background="surface-secondary" radius="lg" padding={3} width="100%" align="center">
    **PSG analog output → STE audio circuitry**
  </box>
</box>

The CPU programs the PSG; the chip then generates the sound autonomously until software changes its registers. Its output is mono at the source, so it does not provide three independent stereo voices. The STE's audio circuitry can combine the PSG signal with the separate stereo DMA sound. 

##### 3.4 How the CPU accesses the PSG

The PSG has a register-select interface and a data interface:

- `$FFFF8800`: select a PSG register when written; reading this address is used to read the selected register.
- `$FFFF8802`: write data to, or read data from, the selected register.

For example, to program channel A's tone period, the CPU selects register 0 and writes its low byte, then selects register 1 and writes the upper four bits. It can then configure register 7 to enable the desired sources and register 8 to set the channel's amplitude.

There is an important emulator detail here: **the PSG's registers 14 and 15 are also I/O ports**, not sound channels. Register 14 is connected to system-control signals including floppy-drive selection, side selection and other machine-specific controls. Emulating only its audio functions would miss some hardware behavior. 

---

#### 4. The STE's DMA sound: playing digitized samples

This is the major sound enhancement over the original ST.

Instead of asking a sound chip to synthesize a waveform, software stores sound samples in RAM. The STE's DMA sound hardware fetches those bytes automatically at a programmable rate, converts them into analog signals, and sends them through the audio filters and mixer. The CPU sets up playback but does not have to write every sample individually. 

##### 4.1 The complete DMA audio pipeline

<box border radius="xl" padding={3} gap={2} align="center">
  <box background="surface-secondary" radius="lg" padding={3} width="100%" align="center" gap={1}>
    **ST-RAM**
    <text size="sm" textAlign="center">Interleaved or channel-organized sample bytes in memory</text>
  </box>
  <icon name="arrow-down" size="lg" />
  <box border radius="lg" padding={3} width="100%" align="center" gap={1}>
    **GSTMCU + GST Shifter sound DMA**
    <text size="sm" textAlign="center">Address counters, memory fetches, sample-rate timing</text>
  </box>
  <icon name="arrow-down" size="lg" />
  <box border radius="lg" padding={3} width="100%" align="center" gap={1}>
    **Left/right sample latches**
    <text size="sm" textAlign="center">Hold the current 8-bit values</text>
  </box>
  <icon name="arrow-down" size="lg" />
  <grid columns={2} gap={2}>
    <grid-item>
      <box background="surface-secondary" radius="lg" padding={2} align="center" gap={1}>
        **Left DAC**
        <text size="sm">8-bit digital → analog</text>
      </box>
    </grid-item>
    <grid-item>
      <box background="surface-secondary" radius="lg" padding={2} align="center" gap={1}>
        **Right DAC**
        <text size="sm">8-bit digital → analog</text>
      </box>
    </grid-item>
    <grid-item>
      <box border radius="lg" padding={2} align="center" gap={1}>
        **Left filtering**
      </box>
    </grid-item>
    <grid-item>
      <box border radius="lg" padding={2} align="center" gap={1}>
        **Right filtering**
      </box>
    </grid-item>
  </grid>
  <icon name="arrow-down" size="lg" />
  <box background="surface-secondary" radius="lg" padding={3} width="100%" align="center" gap={1}>
    **LMC1992 and output stages**
    <text size="sm" textAlign="center">Mixing, volume, balance, bass and treble</text>
  </box>
  <icon name="arrow-down" size="lg" />
  <text weight="medium">Left and right analog audio outputs</text>
</box>

The DMA sound function is integrated into the GST Shifter, while the GSTMCU manages the address and timing side of the transfer. On the motherboard, the analog section includes 74F374 sample latches, DAC0802 converters, LF347 op-amp stages, MF4 switched-capacitor filters and the LMC1992 audio processor. 

##### 4.2 Sample format and stereo organization

DMA sound uses **8-bit PCM samples**. A sample represents the instantaneous signal level at one moment in time. Unlike PSG tone generation, the sample data is an explicit waveform.

For the usual signed-PCM interpretation, the values range from \(-128\) to \(+127\). The STE hardware consumes bytes and produces left and right sample outputs. Software must arrange the data in the format expected by the selected playback mode; mono and stereo playback are not interchangeable simply by changing the output connector.

In stereo mode, the two sample streams represent the left and right channels. A mono program can use the mono mode to feed a single signal to both sides as intended by the hardware mode. In practice, music and games often use DMA playback for sampled instruments, speech, drums, and digitized sound effects, while retaining the PSG for synthesized effects or music. 

##### 4.3 The four DMA sample rates

The STE supports four nominal sample rates:

| Mode bits | Nominal sample rate | Approximate interval between samples |
|---|---:|---:|
| `00` | 6,258 Hz | 159.8 µs |
| `01` | 12,517 Hz | 79.9 µs |
| `10` | 25,033 Hz | 39.9 µs |
| `11` | 50,066 Hz | 20.0 µs |

These are the familiar STE rates, not arbitrary PC-style audio rates. They are derived from the dedicated 8 MHz sound clock and the Shifter's divider logic. 

The higher the rate, the more frequently the hardware fetches and outputs sample values:

- **6.258 kHz:** low bandwidth, low data rate; useful for speech or low-fi effects.
- **12.517 kHz:** better reproduction of higher frequencies.
- **25.033 kHz:** common for more detailed sampled effects and music.
- **50.066 kHz:** highest supported rate, with higher bandwidth and memory consumption.

The theoretical byte rate depends on channel mode. For example, if stereo means one byte per channel per sample instant, then at approximately 25,033 sample instants per second, the raw audio payload is about \(50{,}066\) bytes per second. This is before considering any memory-fetch scheduling overhead.

##### 4.4 DMA registers

The principal DMA sound registers live in the `$FFFF8900` range. These are distinct from the floppy/hard-disk DMA registers.

| Address | Function |
|---|---|
| `$FFFF8901` | Sound DMA control: disabled, play once, or repeat |
| `$FFFF8903`, `$FFFF8905`, `$FFFF8907` | Frame start/base address, high/middle/low |
| `$FFFF8909`, `$FFFF890B`, `$FFFF890D` | Current address counter, read-only |
| `$FFFF890F`, `$FFFF8911`, `$FFFF8913` | Frame end address, high/middle/low |
| `$FFFF8921` | Sound mode: mono/stereo and sample rate |
| `$FFFF8922` | Microwire serial data register for audio control |
| `$FFFF8924` | Microwire mask/clock register |

These are byte-wide registers at odd addresses in the 68000's memory map, with address bytes arranged across the register triplets. The lowest address byte has an alignment restriction: the hardware does not support completely arbitrary odd-byte sample addresses. 

The start, current and end addresses have different purposes:

- **Start/base address:** where playback begins.
- **Current address counter:** where the DMA engine is currently reading.
- **End address:** the boundary at which playback stops or loops.

In one-shot mode, the engine plays the programmed frame and stops. In repeat mode, it loops the frame. Software can also chain playback behavior by reprogramming addresses and control bits.

##### 4.5 What happens on each sample tick?

At a high level, the sound DMA state machine does the following:

1. The sample-rate divider reaches its next playback event.
2. The sound DMA logic requests the next sample data from RAM.
3. The memory system performs the fetch; this has to coexist with CPU and video memory activity.
4. The fetched byte or bytes are captured in the sound output path.
5. The current-address counter advances.
6. The DAC converts the new digital value into an analog level.
7. Analog filtering smooths the stepped waveform.
8. The filtered output is routed through the audio processing and output circuitry.

The DMA engine repeats this sequence without requiring one CPU interrupt per sample. It can raise or expose activity/status transitions at frame boundaries, which software can use for synchronization. The `DMA-active` indication can be connected to MFP Timer A for event counting. 

An emulator should model the *observable sequence and timing*, not assume that every sample is fetched in a simple isolated CPU-like transaction. Video and audio DMA share memory resources, and bus timing matters for accurate reproduction of the complete machine.

---

#### 5. From sample bytes to a real analog waveform

A digital sample is not itself a continuous audio signal. The STE's analog circuitry is important to how playback actually sounds.

##### 5.1 DAC conversion

Each stereo channel has a sample latch and a DAC. The latch holds the current byte stable, while the DAC converts its numerical value into a corresponding analog level.

When the next sample arrives, the output level changes. Viewed on an oscilloscope, the raw signal resembles a staircase: each level is held until the next sample.

The STE's sample latches and DACs are external components, not merely a software abstraction inside the GST Shifter. The motherboard design uses DAC0802 converters and associated analog stages for the left and right channels. 

##### 5.2 Filtering and alias suppression

The staircase contains high-frequency components in addition to the desired sound. The STE uses a switched-capacitor low-pass filter whose cutoff tracks the selected sample rate, followed by a fixed low-pass stage. This suppresses unwanted high-frequency content and reduces aliasing.

The sample-rate-dependent filtering is significant. If an emulator outputs the sample values directly at the correct rate but omits all filtering, it may sound harsher and brighter than the original machine. 

For high fidelity, the emulator should approximate:

- the sample-and-hold behavior of the output latch;
- the sample-rate-dependent low-pass response;
- the fixed high-frequency low-pass response;
- the stereo output path and volume/tone processing.

A reasonable first implementation can use digital filters that approximate the hardware's frequency response. Exact emulation of the switched-capacitor filter is a later refinement.

##### 5.3 The LMC1992: volume, balance and tone

The LMC1992 is the audio processor that controls the final sound characteristics. Software programs it over the STE's Microwire interface. Its controls include master volume, left/right balance, bass, treble and the mix between the PSG and DMA audio. 

The key Microwire registers are:

- `$FFFF8922`: data/command word;
- `$FFFF8924`: mask/clock control.

This interface is not the same as the PSG's register-select/data interface. In particular, a correct emulator must keep the PSG register state separate from the LMC1992 audio-control state.

The LMC1992 has source-selection/mixing behavior as well as tone controls. Therefore, **a program can change the perceived sound without changing any PSG registers or sample bytes**. If the emulator ignores Microwire commands, audio can be too loud, too quiet, tonally incorrect, or missing one source. 

---

#### 6. Does the STE have an “internal speaker” control?

There is a potentially confusing piece of documentation around PSG register 14: some descriptions list a machine-specific bit as an “internal speaker” control. That wording should not be taken as proof that a normal 520STE or 1040STE has a built-in loudspeaker. The standard STE audio hardware is documented as providing audio through its connectors and monitor path, and the motherboard audio circuit routes the sound to external outputs. 

For an emulator, the safe approach is to model the PSG port's documented STE-specific behavior separately from the physical audio output. If your target is specifically a standard CA4003290 STE, don't add an imagined internal speaker or a speaker-power amplifier unless you are deliberately emulating a nonstandard modification.

---

#### 7. A practical emulator implementation plan

For the STE configuration you've chosen, I'd implement sound in layers:

<list marker="number" gap={2}>
  <list-item>**YM2149 core:** 16 registers, three tone counters, noise generator, mixer, amplitude and envelope behavior, plus I/O port side effects.</list-item>
  <list-item>**DMA sound engine:** start/current/end addresses, control modes, mono/stereo behavior, four sample rates, and looping/stop semantics.</list-item>
  <list-item>**Clock and memory timing:** use the STE sound clock and schedule DMA events with the rest of the machine's timing model.</list-item>
  <list-item>**Analog audio model:** separate left/right DAC outputs, sample-and-hold, rate-dependent filtering, fixed filtering, and source mixing.</list-item>
  <list-item>**Microwire/LMC1992:** master volume, balance, bass, treble and PSG/DMA mixing selection.</list-item>
  <list-item>**Output resampling:** convert the emulator's internally timed audio stream to the host audio device's rate without losing event timing.</list-item>
</list>

The most common simplification that causes audible inaccuracies is to implement only the YM2149. That will reproduce much of the original ST's PSG music, but it will not reproduce the STE's sampled music and effects. The second common mistake is to implement DMA samples but omit the LMC1992 and filters.

For the underlying board-level circuit, the most useful references are the [Atari STE hardware description](https://info-coach.fr/atari/hardware/STE-HW.php), the [STE DMA sound overview](https://temlib.org/AtariForumWiki/index.php/Introduction_to_the_STE), and the [STE DMA audio repair article with schematic component details](https://retrotechwiki.com/Atari_1040STE_DMA_Sound_Repair). The repair article is particularly useful for verifying the real DAC, latch, filter and audio-processor stages.

---

## Turn 27 — 2026-10-28 13:38 UTC

### Question

> so the sound DMA is more like the video ram access, plugged to the ram data bus, and not a DMA which negociates cpu data bus access like the disk DMA

### Answer

**Yes, broadly — but with an important distinction:** STE sound DMA is much closer to the video Shifter's memory-fetch mechanism than to the floppy-disk DMA controller's interface. However, sound DMA is still a real DMA mechanism: it reads sample data from RAM without the CPU copying each byte.

The key distinction is that the STE's sound DMA is **integrated into the GST Shifter/GSTMCU video-and-memory architecture**, whereas floppy DMA uses a separate DMA controller and the WD1772 floppy controller. Both ultimately need access to system RAM, but they do not use the same hardware path or control protocol.

#### Comparing the three mechanisms

| Property | Video fetch | STE sound DMA | Floppy disk DMA |
|---|---|---|---|
| Purpose | Fetch pixels for display | Fetch PCM sample bytes | Transfer disk-sector data |
| Main hardware | Shifter + GSTMCU/MMU | GST Shifter + GSTMCU | Separate DMA controller + WD1772 |
| Access to RAM | Scheduled video-memory fetches | Scheduled sample-memory fetches | DMA controller performs bus transfers |
| CPU copies each byte? | No | No | No |
| Timing constraint | Must meet pixel/scanline deadlines | Must meet sample playback deadlines | Must follow floppy data-transfer timing |
| Address progression | Video address counter | Sound DMA address counter | DMA controller address registers |
| Main interaction with CPU | Shared memory/bus scheduling | Shared memory/bus scheduling | Bus arbitration and DMA transfer protocol |

#### The crucial distinction: which bus is being used?

The STE has a useful architectural separation:

- The **68000 system bus** carries CPU addresses and data for normal CPU memory and peripheral accesses.
- The **RAM interface** is controlled by the GSTMCU memory controller, which generates the multiplexed DRAM addresses.
- The **GST Shifter has its own data paths**, including a dedicated 8-bit sound-output bus that carries sample data toward the DAC circuitry.

The STE hardware description explicitly says that the Shifter obtains RAM data without going through the processor, and that the GSTMCU maintains the sound-DMA counters and supplies the sound-fetch control signals. 

The important nuance is that the sound circuitry does **not** have a private copy of RAM. Its fetches still consume real DRAM access time, so the memory controller must coordinate them with video fetches, disk DMA, and CPU RAM accesses.

##### Conceptual sound-fetch sequence

<box border radius="lg" padding={3} gap={2}>
  <row align="center" gap={2}>
    <box background="surface-secondary" radius="md" padding={2} width="88px" align="center">
      GSTMCU
    </box>
    <icon name="arrow-right" />
    <box flex="1">Advances the sound address counter and issues a sound request (`SREQ`).</box>
  </row>
  <divider color="subtle" />
  <row align="center" gap={2}>
    <box background="surface-secondary" radius="md" padding={2} width="88px" align="center">
      Memory controller
    </box>
    <icon name="arrow-right" />
    <box flex="1">Generates the RAM address and controls the DRAM read cycle.</box>
  </row>
  <divider color="subtle" />
  <row align="center" gap={2}>
    <box background="surface-secondary" radius="md" padding={2} width="88px" align="center">
      GST Shifter
    </box>
    <icon name="arrow-right" />
    <box flex="1">Captures the fetched word using the sound-load control (`*SLOAD`) and presents the sample bytes in sequence.</box>
  </row>
  <divider color="subtle" />
  <row align="center" gap={2}>
    <box background="surface-secondary" radius="md" padding={2} width="88px" align="center">
      DACs
    </box>
    <icon name="arrow-right" />
    <box flex="1">Convert the left and right sample values into analog signals.</box>
  </row>
</box>

The names `SREQ` and `*SLOAD` are especially revealing: the Shifter requests sound data, and the GSTMCU coordinates the RAM fetch and signals when the data is available. That is structurally much closer to the video-fetch mechanism than to the floppy controller's transfer handshake. 

By contrast, the **floppy DMA controller** uses the GSTMCU to obtain system-bus access and transfer data between RAM and the WD1772 or an ACSI device. Its `RDY` handshake coordinates the transfer, and the GSTMCU arbitrates between the CPU and DMA controller. 

So, for your emulator, I would model these as two distinct mechanisms:

1. **Video and sound:** memory-controller-scheduled fetches, with separate video and sound address state and separate Shifter capture/output logic.
2. **Disk DMA:** an independent DMA controller that performs transfers through the system bus under GSTMCU arbitration.

One final caution: don't assume sound fetches can never delay CPU memory accesses. The key difference is *how* the fetch is scheduled and routed, not that sound DMA gets free access to RAM.

---

## Turn 28 — 2026-10-28 13:44 UTC

### Question

> you listed earlier 16 external connectors for the atari ste. can we group them by the internal component to which they are connected ?

### Answer

Yes. This is a very useful way to document the STE: group each external connector by the **internal chip or circuit that handles its signals**, rather than by where the connector happens to sit on the case.

The main caveat is that some connectors cross several chips. For example, the monitor connector carries signals from the video subsystem and the audio circuit, while the ACSI connector is handled by both a DMA controller and the system memory/bus-control logic.

The Atari STE service manual and hardware references document the standard connector set and the corresponding internal interfaces. 

#### Connector-to-hardware map

#### 1. Connectors grouped by internal hardware

<box gap={3}>
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE 68000 motherboard GSTMCU Shifter video circuitry" aspectRatio="4:3" maxWidth="112px"/>
    <box flex="1" gap={1}>
      **A. Video subsystem — GSTMCU, GST Shifter and video output circuitry**

      - **Monitor connector** — 13-pin DIN: RGB video, horizontal and vertical sync, monochrome video, and an audio signal.
      - **TV output** — RCA-style connector on versions equipped with the RF modulator; the video encoder/modulator generates the television signal.

      The monitor connector is the direct display interface; the TV output is a separately conditioned signal path. 
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE motherboard YM2149 PSG LMC1992 DAC0802 audio circuitry" aspectRatio="4:3" maxWidth="112px"/>
    <box flex="1" gap={1}>
      **B. Sound subsystem — YM2149, DMA sound circuitry and LMC1992**

      - **Audio left RCA output**
      - **Audio right RCA output**
      - **Monitor connector audio pin** — shares the monitor connector with video signals.

      The PSG generates synthesized sound; STE DMA audio adds sample playback; the analog circuitry mixes and conditions the signals. 
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE motherboard MC68901 MFP serial port line driver IC" aspectRatio="4:3" maxWidth="112px"/>
    <box flex="1" gap={1}>
      **C. Serial subsystem — MC68901 MFP and RS-232 line drivers**

      - **Modem/RS-232 connector** — 25-pin D-sub.

      The MFP provides the serial interface logic and status/handshake functions; line-driver and receiver circuits convert between internal logic levels and RS-232 electrical levels. 
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE motherboard MC6850 ACIA MIDI interface circuitry" aspectRatio="4:3" maxWidth="112px"/>
    <box flex="1" gap={1}>
      **D. MIDI subsystem — MC6850 ACIA and MIDI interface circuitry**

      - **MIDI IN** — 5-pin DIN.
      - **MIDI OUT/THRU** — 5-pin DIN.

      The ACIA handles serial data; the associated current-loop circuitry provides the MIDI electrical interface. MIDI is a data interface, not an audio output. 
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE motherboard DMA controller WD1772 floppy disk controller" aspectRatio="4:3" maxWidth="112px"/>
    <box flex="1" gap={1}>
      **E. Disk and storage subsystem — DMA controller, WD1772 and GSTMCU**

      - **ACSI hard-disk connector** — 19-pin D-sub.
      - **External floppy connector** — 14-pin DIN.
      - **Internal floppy-drive connection** — inside the case, not an external connector.

      ACSI uses the DMA controller and system bus; floppy operations use the WD1772 plus DMA and drive-control signals. The GSTMCU participates in memory access and bus coordination. 
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE motherboard enhanced joystick port controller logic" aspectRatio="4:3" maxWidth="112px"/>
    <box flex="1" gap={1}>
      **F. Controller subsystem — IKBD, STE joystick interface and paddle inputs**

      - **Mouse/joystick port 0** — 9-pin D-sub.
      - **Joystick port 1** — 9-pin D-sub.
      - **Enhanced controller port A** — 15-pin D-sub.
      - **Enhanced controller port B** — 15-pin D-sub.

      The standard mouse and joystick inputs are handled through the keyboard controller (IKBD) and its communication interface. The enhanced STE ports additionally expose extra buttons, directional inputs and analog paddle signals through the STE-specific controller interface. 
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE motherboard 40 pin cartridge slot ROM connector" aspectRatio="4:3" maxWidth="112px"/>
    <box flex="1" gap={1}>
      **G. Expansion bus — 68000 system bus**

      - **ROM cartridge connector** — 40-contact edge connector.

      This is a direct expansion interface to the computer's address and data bus, with associated control signals. Cartridge ROM can be mapped into the CPU's address space; the connector is not handled by a dedicated peripheral controller in the same way as MIDI or floppy I/O. 
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE motherboard parallel printer connector Centronics interface" aspectRatio="4:3" maxWidth="112px"/>
    <box flex="1" gap={1}>
      **H. Parallel interface — PSG I/O and associated control logic**

      - **Printer connector** — 25-pin D-sub.

      The parallel data interface uses programmable I/O associated with the YM2149 PSG, along with control signals for the printer handshake. This is an example of a connector whose function is spread across a chip's I/O ports and other logic rather than handled by a standalone printer-controller IC. 
    </box>
  </row>
  <divider color="subtle"/>
  <row align="start" gap={3}>
    <AsyncImage query="Atari STE motherboard internal power supply IEC inlet" aspectRatio="4:3" maxWidth="112px"/>
    <box flex="1" gap={1}>
      **I. Power subsystem — power supply and reset circuitry**

      - **AC mains inlet** — IEC connector.
      - **Reset button** — case-mounted control, not a data connector.

      The mains inlet feeds the internal power supply, which generates the DC rails used by the CPU, memory, video, audio and peripheral circuitry. Reset is distributed to the system's reset inputs. Neither belongs to a single peripheral chip. 
    </box>
  </row>
</box>

#### 2. Compact reference table

Here is the same information without the explanatory detail.

| External connector | Primary internal component or circuit |
|---|---|
| Monitor, 13-pin DIN | GST Shifter/GSTMCU + video output circuitry |
| TV/RF output, RCA | Video encoder and RF modulator, where fitted |
| Audio Left, RCA | STE DAC/filter/audio processor circuitry |
| Audio Right, RCA | STE DAC/filter/audio processor circuitry |
| RS-232 modem, DB-25 | MC68901 MFP + RS-232 line drivers |
| Printer, DB-25 | YM2149 I/O + printer handshake logic |
| ACSI hard disk, DB-19 | DMA controller + GSTMCU/system bus |
| External floppy, DIN-14 | WD1772 + DMA controller + drive-control logic |
| MIDI IN, DIN-5 | MC6850 ACIA + MIDI input circuit |
| MIDI OUT/THRU, DIN-5 | MC6850 ACIA + MIDI output/THRU circuit |
| Mouse/joystick 0, DE-9 | IKBD and its input circuitry |
| Joystick 1, DE-9 | IKBD and its input circuitry |
| Enhanced controller A, DA-15 | STE enhanced controller interface |
| Enhanced controller B, DA-15 | STE enhanced controller interface |
| Cartridge, 40-pin edge | 68000 address/data bus and control logic |
| AC mains, IEC | Internal power supply |

The reset button is an additional physical control, and the internal floppy connector is not externally accessible. The TV/RF connector is also configuration-dependent, so its presence should not be assumed for every regional or board variant. 

#### 3. What this means for your emulator architecture

I'd group the emulated I/O devices by the hardware that actually owns their behavior:

- **GSTMCU/Shifter:** video, video timing, sound DMA timing, memory coordination.
- **DMA controller + WD1772:** floppy and ACSI transfers.
- **YM2149:** PSG sound and its programmable I/O ports, including relevant printer and drive-control functions.
- **MC68901 MFP:** serial interface, timers, interrupt inputs and general-purpose I/O.
- **MC6850 ACIAs:** MIDI and keyboard-controller serial communication.
- **IKBD:** keyboard, mouse and standard joystick protocol.
- **STE enhanced-controller logic:** extra buttons, directions and paddle/analog readings.
- **Audio analog model:** stereo DACs, filters, source mixing and LMC1992 controls.
- **System bus and expansion:** cartridge mapping, reset and power-on state.

The main architectural lesson is that **a physical connector is not necessarily owned by one chip**. ACSI, floppy, video and audio are the clearest examples of interfaces that span multiple hardware blocks. The grouping above is a functional map; for exact net-by-net ownership and pin assignments, the STE schematic is the final authority.

---

## Turn 29 — 2026-10-28 14:02 UTC

### Question

> your division in subsystems A to I is PERFECT ! Keep it and add a RAM access/refresh subsystem, a CPU + data/address buses + address mapping and chip select subsystem, and a coordination subsystem with clocks, interrupts management, cpu buses arbitration. then dispatch the atari ste components in these subsystems with the pins concerned (ie a component can contribute to several subsystems, but each time with different pins : we pretend that we can split functionnaly the physical ics)

### Answer

### Atari STE functional subsystem map — A through L

We'll keep your original subsystems **A–I unchanged in purpose**, then add:

- **J. RAM access, refresh and memory-cycle generation**
- **K. CPU, address/data buses, address mapping and chip selects**
- **L. System coordination: clocks, interrupts, bus arbitration and reset**

The key modeling rule is yours: **split physical ICs into functional blocks**. For example, the GSTMCU will appear in video, RAM, bus-decoding and coordination sections, but each appearance will list only the signals relevant to that function.

I'll use *signal names and signal groups* rather than inventing physical pin numbers. Exact package pin numbers should be taken from the schematic for the specific board revision. The STE service manual contains separate schematic sheets for the CPU, memory, GSTMCU, Shifter, DMA, audio and peripheral circuits. 

#### How to read the pin lists

- `D0–D15` means the 16-bit system data bus.
- `A1–A23` means the 68000's externally presented address lines; the original 68000 has a 24-bit address space, but the address bus does not expose a conventional `A0` pin.
- A leading `*`, such as `*AS`, means the signal is **active-low**.
- `I/O` indicates a bidirectional or direction-dependent connection.
- “Board-dependent” means the external connector or particular supporting circuit can differ by machine configuration or motherboard revision.

---

#### A. Video subsystem

**Purpose:** generate pixel data, video timing, palette output and the monitor-facing signals.

| Component / functional block | Relevant signals and pins |
|---|---|
| GST Shifter — video input and pixel output | `D0–D15` RAM data input; video clock input; resolution and scrolling control; RGB output signals; horizontal/vertical timing interface; display-enable and blanking controls |
| GSTMCU — video timing | Shifter/GSTMCU clock interface; `DE`; `*BLANK`; `HSYNC`; `VSYNC`; display timing and video-fetch control |
| GSTMCU — video address control | Video counter/address signals and the controls that coordinate video memory fetches |
| RAM interface shared with J | DRAM data and address signals used by scheduled video fetches |
| Monitor connector | RGB, horizontal sync, vertical sync, monochrome video and audio, according to the monitor interface |

The Shifter consumes data fetched from RAM, serializes bitplanes into pixels, applies palette selection and emits the video signal. The GSTMCU supplies the timing and memory coordination. Neither chip should be modeled as an isolated “video card.” 

#### B. Sound subsystem

**Purpose:** synthesize sound, fetch digital samples and produce analog audio.

| Component / functional block | Relevant signals and pins |
|---|---|
| YM2149 PSG | CPU data bus `D0–D7`; register-select and chip-select signals; read/write control; three analog sound outputs; parallel I/O port signals |
| GST Shifter — DMA audio | RAM data input; sound fetch/load control; sound clock; sample output/latch interface |
| GSTMCU — sound DMA control | Sound address counter and request/control signals; sample-rate timing; memory-access coordination |
| Stereo sample latches and DACs | Digital sample-byte inputs; latch control; analog left/right outputs |
| Audio filters and op-amps | Analog audio input/output connections and supply rails |
| LMC1992 | Microwire serial data and clock/control signals; analog audio inputs and outputs |
| Audio connectors | Left and right line-level outputs; monitor connector's audio signal |

The PSG and DMA sound must remain distinct logical devices. The GST Shifter and GSTMCU contribute to sample fetching, but the DACs and analog filters are separate hardware. 

#### C. Serial subsystem — RS-232

**Purpose:** asynchronous serial communications through the modem connector.

| Component / functional block | Relevant signals and pins |
|---|---|
| MC68901 MFP — UART | Serial transmit and receive signals; UART clock inputs; transmit/receive status and control |
| MC68901 MFP — register interface | `D0–D7`; register-address inputs; read/write; chip select; data strobe; interrupt output |
| RS-232 line driver and receiver | Logic-level transmit/receive signals; RS-232 voltage-level outputs/inputs; supply rails |
| Modem connector | Transmit data, receive data, and applicable handshake/control lines such as RTS, CTS, DTR and carrier detect |

The MFP performs serial and timer functions, while the external line drivers translate the voltage levels. The MFP does not connect directly to the RS-232 connector at raw logic levels. 

#### D. MIDI subsystem

**Purpose:** MIDI serial communication with musical equipment.

| Component / functional block | Relevant signals and pins |
|---|---|
| MC6850 ACIA — MIDI | `D0–D7`; register select; `R/W`; chip select; clock; serial transmit and receive; interrupt |
| MIDI input circuit | MIDI current-loop input and optocoupler output toward the ACIA |
| MIDI output circuit | ACIA transmit signal and current-loop output driver |
| MIDI connectors | MIDI IN and MIDI OUT/THRU signal connections |

The ACIA handles bytes and serial timing. The MIDI interface circuitry provides the electrical current-loop behavior, and the MFP handles the broader system interrupt/timer functions. MIDI is a data interface, not an audio waveform output. 

#### E. Disk and storage subsystem

**Purpose:** control floppy drives and communicate with ACSI hard-disk controllers.

| Component / functional block | Relevant signals and pins |
|---|---|
| DMA controller | System-side address/data and bus-control interface; DMA address registers; disk-side data/control interface; transfer status |
| WD1772 FDC | Register data; register selection; read/write control; clock; disk data input/output; `INTRQ`; drive-control and status signals |
| GSTMCU | DMA/bus coordination, memory access timing and relevant completion/interrupt coordination |
| YM2149 — drive control | PSG port bits used for drive selection, side selection and associated drive-control functions |
| MFP | Floppy/DMA interrupt-related input and interrupt-management signals |
| External floppy connector | Read data, write data, index, drive select, side select, motor on, step, direction, track-zero and write-protect signals |
| ACSI connector | ACSI data lines, command/status handshakes and device-selection signals |

**Important distinction:** the WD1772 handles floppy-disk protocol; the separate DMA controller transfers data between memory and the disk-side interface. The GSTMCU coordinates the relevant memory and bus operations. 

#### F. Controller subsystem — keyboard, mouse, joysticks and paddles

**Purpose:** accept user input and report it to software.

| Component / functional block | Relevant signals and pins |
|---|---|
| Keyboard ACIA (MC6850) | CPU data bus `D0–D7`; register select; read/write; chip select; serial receive/transmit; clock; interrupt |
| IKBD microcontroller (inside keyboard) | Keyboard matrix scanning; mouse quadrature inputs; joystick inputs; serial communication with the host |
| GSTMCU — enhanced-controller logic | Enhanced joystick input signals and the associated control/timing interface |
| LM556 paddle timing circuits | Trigger/charge timing and paddle measurement signals |
| Standard mouse/joystick connectors | Direction switches, mouse movement signals and buttons, according to port assignment |
| Enhanced STE controller ports | Additional digital button/direction signals and analog paddle inputs |

The standard keyboard/mouse protocol is handled by the intelligent keyboard controller and its serial connection to the host. The enhanced STE ports introduce additional logic, including paddle timing, rather than simply being extra inputs on the original joystick interface. 

#### G. Expansion subsystem — cartridge

**Purpose:** expose a CPU-addressable expansion interface.

| Component / functional block | Relevant signals and pins |
|---|---|
| Cartridge connector | Address lines, data lines, read/write and bus-control signals, power and ground |
| MC68000 | Generates the address and data transactions used to access cartridge space |
| GSTMCU | Address decoding and relevant chip-select/control logic |
| Cartridge ROM or peripheral | Responds to the selected address range and the associated read/write transaction |

The cartridge connector is not a separate peripheral controller. It extends the machine's address/data interface to external hardware. The exact decoding and available control signals must be taken from the STE schematic rather than assumed from a generic 68000 expansion bus. 

#### H. Parallel subsystem — printer

**Purpose:** provide a Centronics-style parallel printer interface.

| Component / functional block | Relevant signals and pins |
|---|---|
| YM2149 — parallel I/O | PSG port data bits and port-direction/control register behavior |
| Supporting logic | Printer control and handshake signals, including applicable strobe and status paths |
| Printer connector | Eight parallel data bits plus control/status and ground signals |
| CPU / GSTMCU | PSG register access through the normal memory-mapped peripheral interface |

The YM2149's parallel I/O port is part of the printer and system-control interface, not a separate printer-controller IC. Some PSG port bits also serve drive-control functions, so the emulator must preserve those side effects. 

#### I. Power subsystem

**Purpose:** supply and distribute the electrical power needed by the system.

| Component / functional block | Relevant signals and pins |
|---|---|
| AC mains inlet | Mains line, neutral and protective earth as applicable to the power supply |
| Power supply | DC supply outputs and grounds |
| On-board regulators | Regulated rails, including rails used by analog and RS-232 circuitry |
| IC power pins | `VCC`, ground and any device-specific analog supply pins |
| Reset circuitry | Power-on reset and reset distribution signals |

The power subsystem is physically separate from the CPU's memory map. In an ordinary functional emulator, power rails and analog startup behavior can usually be abstracted away, while reset state and timing still need explicit modeling. The STE service manual describes the supply and board-level regulators. 

---

### New subsystems

#### J. RAM access, refresh and memory-cycle generation

**Purpose:** control the physical DRAM devices and supply data to all clients that need RAM.

This subsystem is central to the STE architecture. The RAM is not simply a passive array directly addressed by the CPU. The GSTMCU contains the memory-management functions that generate the DRAM control timing and coordinate the memory interface with video and DMA activity. 

| Component / functional block | Relevant signals and pins |
|---|---|
| GSTMCU — DRAM address generation | Multiplexed DRAM row/column address outputs; bank selection and associated address controls |
| GSTMCU — DRAM timing | Row address strobe `*RAS`; column address strobe `*CAS`; write-enable `*WE`; memory enable/selection signals, as applicable to the board |
| SIMM DRAM devices | Multiplexed address inputs; data inputs/outputs; `*RAS`; `*CAS`; `*WE`; power and ground |
| RAM data interface | Data lines shared by CPU, video and DMA access paths according to the memory architecture |
| GST Shifter — video client | Requests/consumes video words through scheduled memory-fetch cycles |
| GST Shifter — sound client | Consumes sample data from scheduled sound-DMA fetches |
| DMA controller — disk client | Requests and performs disk transfers through the system's DMA/bus-control path |
| CPU and Blitter — memory clients | Read/write transactions serviced through the memory controller and system bus |

##### What each signal group does

- **DRAM address lines:** the same physical address pins are used at different times for row and column information. This is why the RAM address interface is multiplexed.
- **`*RAS`:** captures the row-address portion of a DRAM access.
- **`*CAS`:** captures the column-address portion and participates in the read/write timing.
- **`*WE`:** controls whether the selected memory operation writes data.
- **Data bus:** transfers the actual contents to or from the selected memory device.
- **Memory-cycle timing:** ensures that addresses, strobes and data satisfy the DRAM's electrical timing requirements.

##### Refresh is a distinct responsibility

Dynamic RAM stores data in capacitors and must periodically be refreshed. In the emulator's *functional* memory model, you normally do not need to emulate capacitor charge decay or every physical refresh pulse. You should, however, model the memory controller's scheduling accurately enough that CPU, video, Blitter and DMA accesses experience the correct observable timing.

I would keep three concepts separate in the implementation:

1. **DRAM protocol:** row/column address multiplexing, strobes and read/write cycles.
2. **Refresh scheduling:** the controller's maintenance of DRAM contents.
3. **Access arbitration/scheduling:** which client receives memory service at a given time.

These are related but not identical functions. In particular, a video fetch is not a CPU bus transaction merely because both ultimately access the same RAM.

#### K. CPU, address/data buses, address mapping and chip selects

**Purpose:** execute instructions, initiate memory and peripheral transactions, and route those transactions to the correct devices.

The central component is the MC68000. The GSTMCU decodes addresses and supplies important control signals, while the Blitter and DMA controller can also participate in memory transfers. The CPU and GSTMCU should therefore be separate logical blocks in your emulator, even though their behavior is tightly coupled. 

| Component / functional block | Relevant signals and pins |
|---|---|
| MC68000 — address bus | `A1–A23`; `FC0–FC2` function codes identifying the access type |
| MC68000 — data bus | `D0–D15`, bidirectional |
| MC68000 — transaction control | `*AS`, `R/W`, `*UDS`, `*LDS`, `DTACK`, `BERR`, `VPA`, `VMA`, `E`, where applicable |
| MC68000 — bus arbitration | `BR`, `BG`, `BGACK` |
| GSTMCU — address decoding | CPU address inputs and decoded device-select outputs |
| GSTMCU — bus-cycle completion | `DTACK` and bus-error generation/control |
| ROM devices | Address inputs, data outputs, chip enable/output enable |
| RAM subsystem | Address and data interface plus DRAM-control signals |
| Peripheral devices | Data bus, address/register selection, read/write and individual chip-select signals |
| Blitter | CPU register interface, memory address/data paths and bus-control interface |

##### The CPU's principal signals

- `A1–A23`: address information. The original 68000 has a 24-bit address space, but `A0` is not exposed as an ordinary external address pin.
- `D0–D15`: the 16-bit data bus.
- `*AS`: indicates that a valid address is being presented for a bus transaction.
- `R/W`: selects read versus write.
- `*UDS` and `*LDS`: select the upper and lower byte lanes. This allows byte accesses without treating the external bus as an 8-bit bus.
- `DTACK`: tells the CPU that the bus transaction has completed.
- `BERR`: indicates a bus error.
- `FC0–FC2`: identify the function code of the access, including user/supervisor and program/data distinctions.
- `BR`, `BG`, `BGACK`: the CPU bus-arbitration handshake. These are distinct from `DTACK`, which completes an individual transaction.

##### Address mapping and chip selection

The GSTMCU's decoding logic determines which hardware responds to a CPU address. Conceptually, it distinguishes regions such as:

- ST-RAM;
- TOS ROM;
- memory-mapped I/O;
- cartridge space;
- invalid or otherwise unsupported accesses.

Within the I/O region, individual select signals and address bits determine which peripheral register is being accessed. A peripheral may receive the CPU's data bus but remain inactive unless its chip-select and transaction-control signals are asserted.

For your emulator, model this as an address decoder followed by device-specific register handlers. Keep the **logical address map** separate from the **electrical bus-cycle timing**: the first decides *which device* is addressed; the second decides *when the access completes*. 

#### L. System coordination — clocks, interrupts, bus arbitration and reset

**Purpose:** make the CPU, memory controller, video, sound, DMA, peripherals and Blitter operate together.

This is the cross-cutting subsystem. It does not replace the functional subsystems above; it describes the shared timing and coordination signals that connect them.

| Component / functional block | Relevant signals and pins |
|---|---|
| Main oscillator | Master clock output, nominally around 32 MHz, with PAL/NTSC-specific frequency differences |
| GST Shifter clock logic | Master clock input; clocks supplied to the GSTMCU; dedicated sound-clock input |
| GSTMCU — clock generation | Clock inputs and divided clock outputs used by the CPU and peripheral subsystems |
| MC68000 — arbitration | `BR`, `BG`, `BGACK` |
| GSTMCU — bus coordination | Bus-grant/arbitration-related logic, memory scheduling and DMA coordination |
| MC68901 MFP — interrupt controller | `IRQ`; interrupt request inputs; interrupt acknowledge and priority-chain signals; timer and serial signals |
| GSTMCU — interrupt generation | Video-related interrupt outputs and associated timing/control signals |
| MC68000 — interrupt interface | `IPL0–IPL2`; interrupt acknowledge cycle; interrupt-level handling |
| WD1772 / DMA-related logic | Floppy interrupt request and DMA-completion signals routed into the interrupt system |
| MC6850 ACIAs | Interrupt request outputs for keyboard/MIDI serial events |
| Reset circuitry | CPU and peripheral reset inputs, plus relevant reset distribution signals |

##### L1. Clock coordination

The STE uses a master oscillator and derived clocks rather than giving every chip an independent unrelated clock. The GST Shifter and GSTMCU participate in generating and distributing the timing needed for the CPU, memory, video and peripherals. A separate sound clock is used by the Shifter's audio circuitry, while the MFP has its own timing reference for its timers. 

In the emulator, keep clock domains conceptually distinct even if you ultimately run them from one master cycle counter. This allows you to schedule video fetches, audio sample events, timer ticks and CPU bus cycles at their appropriate relative times.

##### L2. Interrupt management

The MC68000 samples the three interrupt-level signals `IPL0–IPL2`. The MFP handles multiple peripheral sources and provides interrupt prioritization within its own domain. Video events and peripheral requests reach the CPU through their respective interrupt paths.

The important distinction is that **the MFP is not the only interrupt source**. Video-related interrupts, MFP interrupts and other hardware event paths must be represented according to their actual routing. 

For emulation, model the source event first, then the appropriate interrupt controller or direct interrupt path, then the CPU's interrupt-level sampling and exception entry.

##### L3. Bus arbitration

The 68000's `BR`, `BG` and `BGACK` signals are the CPU's bus-arbitration interface. They should not be confused with `DTACK`, which completes an individual bus transaction.

The GSTMCU and memory system coordinate memory availability among CPU activity, video fetches, sound fetches and DMA operations. The disk DMA controller has its own transfer mechanism, while video and sound fetching are integrated into the GST Shifter/GSTMCU memory architecture.

The Blitter also needs to be included as a memory client when present. Its exact relationship with CPU accesses depends on the operation and the hardware's bus-control behavior. 

##### L4. Reset and startup coordination

Reset is not simply a software variable set to zero. The physical reset circuitry initializes the CPU and relevant devices, and the CPU begins execution through its reset-vector sequence. Peripheral state, clock startup and memory availability affect the early boot process.

For a functional emulator, keep reset distribution and device reset states in this coordination layer, but leave each device's detailed register reset values with its own subsystem.

---

#### Master component-to-subsystem index

This is the compact overview of where each major physical IC contributes. Multiple entries are intentional: we're splitting each IC's functions, not duplicating the physical device.

| Physical component | Functional subsystems |
|---|---|
| MC68000 | K, L |
| GSTMCU | A, B, E, F, G, H, J, K, L |
| GST Shifter | A, B, J, L |
| DRAM SIMMs | A, B, E, J, K |
| Blitter | A, J, K, L |
| DMA controller | E, J, K, L |
| WD1772 | E, L |
| YM2149 PSG | B, E, H, K |
| MC68901 MFP | C, E, F, L |
| MC6850 keyboard ACIA | F, K, L |
| MC6850 MIDI ACIA | D, K, L |
| IKBD microcontroller | F |
| DACs and audio filters | B, I |
| LMC1992 | B, K |
| RS-232 drivers/receivers | C, I |
| MIDI input/output circuitry | D, I |
| Paddle timing circuits | F, I |
| ROMs | G, K |
| Power supply and regulators | I |
| Reset circuitry | I, L |

#### Recommended emulator architecture

I'd implement the twelve subsystems as **functional modules with shared hardware state**, rather than as twelve independent emulated chips:

<box border radius="xl" padding={3} gap={2}>
  <box background="surface-secondary" radius="lg" padding={3} align="center">
    **L. Coordination and timing**
    <text size="sm">Master scheduler, clocks, interrupts, arbitration, reset</text>
  </box>
  <icon name="arrow-down-up" size="lg" />
  <grid columns={2} gap={2}>
    <grid-item>
      <box border radius="lg" padding={3} align="center" gap={1}>
        **K. CPU and bus**
        <text size="sm" textAlign="center">Address map, chip selects, bus cycles</text>
      </box>
    </grid-item>
    <grid-item>
      <box border radius="lg" padding={3} align="center" gap={1}>
        **J. Memory**
        <text size="sm" textAlign="center">RAM access, DRAM timing, refresh</text>
      </box>
    </grid-item>
    <grid-item>
      <box border radius="lg" padding={3} align="center" gap={1}>
        **A–B. Video and sound**
        <text size="sm" textAlign="center">Shifter, pixel and sample pipelines</text>
      </box>
    </grid-item>
    <grid-item>
      <box border radius="lg" padding={3} align="center" gap={1}>
        **C–I. Peripherals**
        <text size="sm" textAlign="center">Serial, MIDI, disk, input, expansion, power</text>
      </box>
    </grid-item>
  </grid>
</box>

The architectural rule I would preserve is this: **K decides what a CPU transaction means; J implements memory service; L determines when competing events and transactions can occur.** A–I then describe the functional devices and interfaces, while calling into those shared services where necessary.

That division should give you a clean conceptual model without losing the STE's defining characteristic: the GSTMCU, Shifter, CPU, DMA controller and Blitter are tightly coordinated hardware, not a collection of independent devices that can all access RAM at arbitrary times.

---

## Turn 30 — 2026-10-28 14:12 UTC

### Question

> ok, now tell me what the blitter is in great details, we missed it completely until now

### Answer

### The Atari STE Blitter — a detailed hardware and emulation guide

The Blitter is a major part of the Atari STE that we should have included earlier. It belongs in **subsystem J (RAM access and memory service), K (CPU bus and address mapping), and L (coordination, timing, and arbitration)**—but it is also a programmable graphics-processing device in its own right.

The original Atari ST Blitter manual describes it as a hardware implementation of the *bit-block transfer* algorithm, usually called **BitBLT**. The important point is that it does much more than copy pixels: it reads and writes words of memory, shifts source bits, applies masks and Boolean logic, and repeats the operation across a rectangular block. 

<AsyncImageGroup query={["Atari STE motherboard Blitter chip close up","Atari Mega ST blitter chip motherboard"]} aspectRatio="5:4" layout="bento" />

#### 1. What the Blitter actually does

Imagine a game needs to draw a character sprite onto a background. The CPU could read every word of the sprite, work out which pixels are transparent, combine the remaining pixels with the background, and write the results back to RAM.

The Blitter is a specialized hardware engine for performing this sort of repetitive work. The CPU describes the operation by writing a collection of registers; the Blitter then performs the memory transfers and bitwise operations itself.

It can perform tasks such as:

- Copying a rectangular area from one memory location to another.
- Copying a sprite while preserving selected destination pixels.
- Combining source and destination data using AND, OR, XOR, and their complements.
- Filling areas with zeros, ones, or a repeated pattern.
- Applying monochrome patterns and masks.
- Shifting source data relative to the destination, allowing bit-aligned copies even when source and destination positions do not line up on 16-bit boundaries.
- Drawing, masking, and combining graphics using sequences of operations.

The Atari ST/STE Blitter works with **16-bit words**, not individual pixel coordinates. It does not understand that a byte represents a pixel or that a word contains four planar pixels. It understands addresses, words, masks, shifts, and Boolean operations. The graphics meaning comes from how software arranges the memory.

That distinction is fundamental for an emulator: the Blitter should be implemented as a memory-processing device, not as a sprite renderer.

#### 2. Where it sits in the STE architecture

<box border radius="xl" padding={3} gap={2}>
  <box background="surface-secondary" radius="lg" padding={3} align="center">
    **68000 CPU**

    <text size="sm" textAlign="center">Programs the Blitter through memory-mapped registers</text>
  </box>
  <box align="center" gap={1}>
    <icon name="arrow-down-up" size="xl" />
    <text color="secondary" size="xs">Register accesses and shared memory bus</text>
  </box>
  <box border radius="lg" padding={3} align="center" gap={1}>
    <title size="lg">Blitter</title>
    <grid columns={2} gap={2}>
      <grid-item>
        <box background="surface" radius="md" padding={2}>
          <text weight="medium" textAlign="center">Address generator</text>
          <text size="xs" textAlign="center">Source and destination stepping</text>
        </box>
      </grid-item>
      <grid-item>
        <box background="surface" radius="md" padding={2}>
          <text weight="medium" textAlign="center">Data processor</text>
          <text size="xs" textAlign="center">Shift, mask, pattern, Boolean logic</text>
        </box>
      </grid-item>
    </grid>
    <text color="secondary" size="sm">Own internal registers and 16-word halftone RAM</text>
  </box>
  <box align="center" gap={1}>
    <icon name="arrow-down-up" size="xl" />
    <text color="secondary" size="xs">Requests and performs memory accesses</text>
  </box>
  <box background="surface-secondary" radius="lg" padding={3} align="center">
    **GSTMCU / memory system / DRAM**

    <text size="sm" textAlign="center">Memory timing and access coordination shared with the CPU, video, and other DMA activity</text>
  </box>
</box>

The Blitter is **not the MMU, not the floppy DMA controller, and not the Shifter**.

- **68000:** executes instructions and configures the operation.
- **Blitter:** calculates source and destination addresses, processes words, and requests memory transfers.
- **GSTMCU:** participates in the STE's memory-control and system-coordination logic.
- **DRAM:** stores the source, destination, and other machine data.
- **Shifter:** independently fetches display data according to video timing.

The Blitter can therefore compete with the CPU and other memory users. It does not have a private copy of system RAM. Its ability to run in parallel with the CPU is limited by bus ownership and the memory system's available access slots. 

One hardware detail matters for your target machine: **the STE Blitter is software-compatible with the Mega ST Blitter, but its physical implementation is not necessarily the same**. Do not infer the exact chip packaging from the programmer-visible register interface. 

#### 3. The Blitter's register map

The Blitter is memory-mapped at `$FFFF8A00`. The CPU configures it by reading and writing ordinary addresses, although the registers have special hardware behavior.

All addresses below are hexadecimal. Most registers are 16-bit words, but the two final control registers are byte registers.

| Address | Register | Purpose |
|---|---|---|
| `$FFFF8A00–$FFFF8A1E` | Halftone RAM | Sixteen 16-bit pattern words |
| `$FFFF8A20` | Source X increment | Address step between source words |
| `$FFFF8A22` | Source Y increment | Additional source step at each line end |
| `$FFFF8A24–$FFFF8A26` | Source address | 24-bit source address in a 32-bit register |
| `$FFFF8A28` | Endmask 1 | Mask for the first destination word of a line |
| `$FFFF8A2A` | Endmask 2 | Mask for intermediate destination words |
| `$FFFF8A2C` | Endmask 3 | Mask for the final destination word |
| `$FFFF8A2E` | Destination X increment | Address step between destination words |
| `$FFFF8A30` | Destination Y increment | Additional destination step at each line end |
| `$FFFF8A32–$FFFF8A34` | Destination address | 24-bit destination address in a 32-bit register |
| `$FFFF8A36` | X count | Number of words per line |
| `$FFFF8A38` | Y count | Number of lines |
| `$FFFF8A3A` | HOP | Halftone/source selection operation |
| `$FFFF8A3B` | OP | Boolean operation |
| `$FFFF8A3C` | Line number / control | Start, busy, bus mode, smudge, pattern row |
| `$FFFF8A3D` | Skew | Source alignment and special source-read controls |

This is the conventional ST/STE programmer-visible map. The address registers occupy three bytes of meaningful address plus a surrounding register layout; do not implement them as unrestricted 32-bit physical addresses. The system uses a 24-bit 68000 address space, and the Blitter's address behavior is constrained accordingly. 

##### The halftone RAM

The first 32 bytes are sixteen 16-bit words stored inside the Blitter. They are not ordinary DRAM addresses; they are a small, dedicated pattern store.

Think of the words as the rows of a 16-by-16 one-bit pattern. For example, a repeating alternating pattern could be represented by words containing alternating `1` and `0` bits. The Blitter can select a row from this store and combine it with source data.

This makes patterned fills and monochrome masking efficient: the pattern is already inside the Blitter, so the hardware does not need to fetch it from main RAM for every word.

The pattern is one bit per position, not a 16-colour bitmap. How it affects the destination depends on HOP and OP.

#### 4. How a blit is described

A **blit** is one programmed operation. The CPU supplies:

1. A source address and destination address.
2. Source and destination X/Y increments.
3. End masks for the destination.
4. The X and Y counts.
5. The halftone operation and Boolean operation.
6. The skew and bus-control settings.
7. A start command.

The Blitter then walks the rectangle, processing a word at a time.

##### X count and Y count

- **X count** is the number of words to process per line.
- **Y count** is the number of lines to process.

For example, if X count is 10 and Y count is 20, the logical rectangle contains 200 word positions. That does not mean 200 memory accesses: some words require source reads, destination reads, and writes, while others can avoid some reads depending on the selected operation.

A zero count is a special case and should not be treated as a normal zero-length rectangle without checking the hardware's behavior. For a first emulator implementation, explicitly model the documented count and start behavior rather than assuming every value has ordinary arithmetic meaning.

##### Source and destination increments

The Blitter works with word-aligned addresses and normally accesses 16-bit words. Its X and Y increments are **byte increments**, not pixel counts.

Conceptually, the source address for a line advances like this:

\[
S_{\text{next line}} =
S_{\text{line start}} + X_{\text{count}} \cdot S_X + S_Y
\]

The destination has a separate set of increments:

\[
D_{\text{next line}} =
D_{\text{line start}} + X_{\text{count}} \cdot D_X + D_Y
\]

These equations describe the intention, not every internal micro-operation or register-update quirk. In practice, the Blitter advances through the words within a line and then applies the line increment. The increments can be signed, which lets software traverse memory in reverse as well as forwards. The source and destination layouts can be different. 

For example, a source image may occupy a wide rectangle in RAM, while the destination is a smaller window inside a larger screen buffer. The X increment steps between the words being copied; the Y increment skips the rest of each row to reach the next row's starting position.

This is why a Blitter emulator cannot simply assume that each row is contiguous or that the source and destination have the same stride.

#### 5. End masks: protecting pixels at the edges

The Blitter's end masks are especially important for graphics.

Each destination word contains 16 bits. But a requested operation might begin halfway through the first word and finish halfway through the last word. Writing all 16 bits would overwrite pixels outside the intended rectangle.

The three end-mask registers solve this:

- **Endmask 1:** first destination word of each line.
- **Endmask 2:** intermediate destination words.
- **Endmask 3:** last destination word of each line.

A mask bit selects whether the corresponding destination bit is eligible to be replaced or combined. A bit set to `1` in a mask enables that position; a `0` preserves it. The resulting word is formed using the Blitter's logical operation and the mask.

A simple conceptual example:

<box border radius="lg" padding={3} gap={3}>
  <text color="secondary" size="xs">Illustrative 16-bit destination word</text>
  <grid columns={16} gap={1}>
    {#each Array.from({length:16},(_,i)=>i) as i}
      <grid-item>
        <box background={i>=4 && i<=11 ? "surface-tertiary" : "surface-secondary"} border radius="xs" padding={{top:2,bottom:2}} align="center">
          <text size="xs">{i>=4 && i<=11 ? "1" : "0"}</text>
        </box>
      </grid-item>
    {/each}
  </grid>
  <text size="sm">Only the selected middle eight bit positions are enabled by this illustrative mask. The other positions are preserved according to the mask/write logic.</text>
</box>

Masks matter most when an operation does not begin and end on word boundaries. A full-word copy with all mask bits enabled can avoid a destination read in cases where no old destination bits need preserving. When masking requires the old destination data, the Blitter must read it before writing the result. This can change the execution time. 

**Emulator implication:** do not implement masks as a final cosmetic step. They affect the data result and can affect which memory accesses are required.

#### 6. Skew: shifting the source stream

The source and destination may start at different bit positions. The Skew register allows the Blitter to shift the source stream by 0–15 bit positions before combining it with the destination.

Suppose a monochrome sprite begins at bit position 5 of a word, but it must be drawn beginning at bit position 9 of the destination. Software could manually shift every source word and merge adjacent words. The Blitter's skew mechanism helps do this in hardware.

The register includes:

- **Bits 0–3:** skew amount.
- **NFSR:** No Final Source Read.
- **FXSR:** Force eXtra Source Read.

The two flags affect source-read behavior at the boundaries of a transfer. They matter because a shifted word may depend on data from adjacent source words. The Blitter's internal source latch and the selected flags determine which source words are fetched, including at the beginning and end of a line.

These controls are not merely performance hints. They can affect the output data and the access sequence, especially for short transfers and nonzero skew. 

For an emulator, begin with correct bit alignment and edge behavior. Add exact fetch sequencing as a separate refinement, because source-read quirks can affect bus timing as well as the final result.

#### 7. HOP and OP: the Blitter's data-processing logic

This is the heart of the device. The Blitter has two stages of logic:

- **HOP** selects or combines the source word and the halftone pattern.
- **OP** combines the resulting word with the old destination word.

Use these conceptual symbols:

- \(S\): source word fetched from RAM.
- \(H\): selected halftone pattern word.
- \(D\): old destination word.
- \(I\): intermediate word produced by HOP.
- \(R\): result written to the destination.

Each equation is applied independently to all 16 bits of the word.

##### HOP — Halftone OPeration

| HOP value | Intermediate result |
|---:|---|
| `0` | All bits set to 1 |
| `1` | Halftone word \(H\) |
| `2` | Source word \(S\) |
| `3` | \(H \land S\) |

HOP is a selector and simple combiner. For a plain source copy, use the source-select mode; for a pattern-only operation, use the halftone-select mode. The exact meaning of the subsequent result still depends on OP. 

##### OP — Boolean operation

OP is a 4-bit Boolean function. It describes how the intermediate word \(I\) and destination word \(D\) combine.

| OP | Result \(R\) | Typical use |
|---:|---|---|
| `0` | \(0\) | Clear destination |
| `1` | \(I \land D\) | AND with existing data |
| `2` | \(I \land \neg D\) | Masked combination |
| `3` | \(I\) | Replace with intermediate data |
| `4` | \(\neg I \land D\) | Clear where intermediate bits are set |
| `5` | \(D\) | Leave destination unchanged |
| `6` | \(I \oplus D\) | XOR effects |
| `7` | \(I \lor D\) | Merge set bits |
| `8` | \(\neg I \land \neg D\) | Complemented combination |
| `9` | \(\neg I \oplus D\) | Complemented XOR |
| `A` | \(\neg D\) | Invert destination |
| `B` | \(I \lor \neg D\) | OR with complemented destination |
| `C` | \(\neg I\) | Write complemented intermediate data |
| `D` | \(\neg I \lor D\) | Complemented merge |
| `E` | \(\neg I \lor \neg D\) | Complemented combination |
| `F` | All bits set to 1 | Set destination |

The register's numeric value is the four-bit function selector. The table above gives the usual Boolean interpretation; exact memory-read optimization depends on the operation and the mask.

##### Why two stages are useful

Suppose you want to draw a one-bit mask over an existing planar image:

1. HOP selects source data or combines it with a halftone pattern.
2. OP combines that intermediate data with the old destination.
3. The end mask limits which destination bits can change.
4. The result is written back to RAM.

With the appropriate settings, the same hardware can perform a direct copy, a masked merge, an XOR effect, a fill, or a patterned operation. This flexibility is why the Blitter is useful even though it does not understand sprites or colours. 

#### 8. Halftone selection and smudge mode

The 16-word halftone RAM provides a repeating pattern. The Line Number field in the control register chooses which row is used.

- Without smudge mode, the selected line number is controlled by the control register's low four bits.
- With **smudge mode** enabled, the low four bits of the first source word are used to select the halftone row.

Smudge mode allows source data to influence the pattern row used by the operation. It is a specialized feature; ordinary rectangular copies often do not need it.

A faithful implementation should keep the halftone RAM, current pattern-row selection, and smudge behavior separate from ordinary source and destination memory. Halftone RAM is internal Blitter state, not a normal address range in DRAM. 

#### 9. The control register: starting, busy state, and bus mode

At `$FFFF8A3C`, the control register has three major functions.

| Bit | Name | Meaning |
|---:|---|---|
| 7 | BUSY / start | Writing 1 starts or resumes the operation; reads as busy while active |
| 6 | HOG | Selects bus-hogging versus cooperative mode |
| 5 | SMUDGE | Selects halftone-row behavior from source data |
| 3–0 | Line number | Initial halftone row |

Bit 4 is unused in the documented register layout. The busy/start behavior also has a pause/resume nuance: writing the busy bit as zero while the CPU owns the bus can pause an ongoing operation, rather than necessarily cancelling it. Exact edge cases should be verified against the original manual and established emulator behavior. 

##### Cooperative mode versus HOG mode

The Blitter has two different approaches to using the memory bus.

<box gap={3}>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="lg" padding={2} size="44px" align="center" justify="center">
      <icon name="arrow-left-right" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **Cooperative (BLiT) mode — HOG = 0**

      The Blitter shares bus access with the CPU. The documented model alternates blocks of bus access opportunities, commonly described as approximately 64 accesses for the Blitter and 64 for the CPU. This is not equivalent to giving each side a fixed number of CPU instructions: memory requests and arbitration determine what actually happens.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="lg" padding={2} size="44px" align="center" justify="center">
      <icon name="gauge" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **HOG mode — HOG = 1**

      The Blitter retains the bus for as much of the transfer as the hardware's rules permit. This can complete the operation sooner, but it prevents the CPU and other bus users from making normal progress during the occupied periods.
    </box>
  </row>
</box>

The bus-mode choice affects more than Blitter completion time. It can affect when the CPU gets to execute memory instructions, when competing devices obtain memory access, and when software observes the completed result. 

**Important for the STE emulator:** don't implement HOG as "make the Blitter faster" and cooperative mode as "make it slower." Model it as a bus-ownership policy. Then let the memory system schedule actual accesses.

#### 10. How the Blitter uses RAM, one word at a time

A Blitter operation is not necessarily one source read followed by one destination write. Depending on the operation, masks, and skew, each word may require different memory transactions.

A simplified sequence for a masked copy might look like this:

<box border radius="xl" padding={3} gap={2}>
  <box border radius="md" padding={2} align="center">
    **1. Fetch source word**

    <text size="sm" textAlign="center">Read the source from DRAM, if the operation needs it.</text>
  </box>
  <box align="center">
    <icon name="arrow-down" />
  </box>
  <box border radius="md" padding={2} align="center">
    **2. Align and process**

    <text size="sm" textAlign="center">Apply skew, select HOP input, calculate the OP result.</text>
  </box>
  <box align="center">
    <icon name="arrow-down" />
  </box>
  <box border radius="md" padding={2} align="center">
    **3. Read destination if needed**

    <text size="sm" textAlign="center">Preserve existing bits for masked or destination-dependent operations.</text>
  </box>
  <box align="center">
    <icon name="arrow-down" />
  </box>
  <box border radius="md" padding={2} align="center">
    **4. Write destination word**

    <text size="sm" textAlign="center">Store the result, advance the address state, and continue.</text>
  </box>
</box>

This is a conceptual sequence, not a claim that every operation executes these exact four steps. Some operations don't need a source read; some don't need a destination read; internal buffering and skew can change the fetch pattern.

The useful timing rule is that **a word transfer can require one, two, or three bus accesses**, depending on the operation and whether the old destination or source must be read. Halftone data comes from internal storage and doesn't itself require a DRAM access. 

That has two implications:

1. A large, straightforward copy can be much faster than a destination-dependent Boolean operation of the same dimensions.
2. A rectangle's dimensions alone aren't enough to calculate its execution time.

##### Interaction with video and other devices

The STE's display hardware needs memory access at specific times to fetch pixels. DMA sound and disk activity also use system memory, while the CPU and Blitter can compete for access.

The Blitter therefore cannot be modelled as an independent worker that reads and writes an unlimited private memory. Its transactions must interact with the system's bus and memory service.

For your emulator, it is helpful to distinguish:

- **Blitter internal progress:** which word or line it is processing.
- **Memory transactions:** which source or destination access is being requested.
- **Bus ownership:** whether the Blitter, CPU, or another requester can use the bus.
- **Video timing:** display fetches that have their own deadlines and coordination.

The exact priority rules and cycle-level ordering are where a high-fidelity emulator becomes more complicated than a functional one.

#### 11. Where it belongs in your A–L subsystem map

Keep the existing A–I grouping unchanged. The Blitter adds detail to three of the later groups:

| Subsystem | Blitter's role |
|---|---|
| **J — RAM access and refresh** | Requests source reads and destination reads/writes; shares DRAM service with other clients |
| **K — CPU bus and address mapping** | Exposes the register block at `$FFFF8A00–$FFFF8A3D`; accesses RAM through the system's memory mapping |
| **L — Coordination, clocks, interrupts and arbitration** | Controls bus sharing versus HOG behavior and determines when its memory accesses can occur |

It also has its own **internal functional block**: address counters, word-processing logic, masks, skew state, halftone RAM, and control registers.

One architectural distinction is important: the Blitter is a memory-bus master, but it is not the same thing as the floppy DMA controller. The floppy controller and WD1772 provide the disk transfer path; the Blitter performs programmed word processing. Both rely on the broader system memory architecture, but their internal protocols and registers are different.

#### 12. How to implement it in your emulator

I recommend building the Blitter in stages rather than trying to reproduce every timing quirk immediately.

<list marker="number" gap={2}>
  <list-item>
    **Stage 1 — Register interface**

    Implement the complete register map, correct byte/word access behavior, source and destination addresses, counts, masks, HOP, OP, control, and skew registers. Keep internal state separate from the raw values written by the CPU.
  </list-item>
  <list-item>
    **Stage 2 — Correct functional results**

    Implement the source and destination address stepping, line counts, end masks, skew, halftone RAM, HOP/OP logic, and the final destination writes. Test forward and reverse traversal, overlapping regions, masked edges, and short transfers.
  </list-item>
  <list-item>
    **Stage 3 — Asynchronous operation**

    A write that starts the Blitter should initiate an operation that can continue while the CPU runs, subject to bus ownership. Model the busy state and the effect of subsequent register accesses while the operation is in progress.
  </list-item>
  <list-item>
    **Stage 4 — Bus contention**

    Route every source and destination access through your shared memory scheduler. Implement cooperative and HOG modes without reducing them to a fixed duration multiplier.
  </list-item>
  <list-item>
    **Stage 5 — Timing accuracy and edge cases**

    Refine word-access costs, skew-related extra reads, end-mask behavior, pause/resume behavior, count corner cases, and the timing of CPU accesses during Blitter activity. Compare results against known hardware tests and established emulator implementations.
  </list-item>
</list>

The Atari Blitter manual is the best starting point for the programming model. The detailed register listing and execution-time analysis are useful complements for emulator-specific corner cases. 

##### What not to do

- Don't render sprites directly into the video buffer as a special graphics shortcut.
- Don't assume all operations read a source word and then write a destination word.
- Don't assume the Blitter always copies forward.
- Don't treat end masks as a purely visual post-processing operation.
- Don't let it bypass memory mapping and shared-bus scheduling.
- Don't assume the CPU is completely halted for every Blitter operation.
- Don't assume all STE motherboard revisions have identical physical Blitter implementations.

#### 13. How much timing accuracy does your target need?

For your **PAL Atari STE with 68000, 4 MB RAM, and TOS 1.62**, I would make the Blitter a first-class hardware device from the beginning. But I'd keep functional correctness and exact bus timing as separate milestones.

A sensible initial implementation is:

- Correct memory-mapped registers.
- Correct bitwise output and address progression.
- Correct busy/start behavior.
- Both cooperative and HOG modes represented explicitly.
- Shared-memory access requests rather than direct, instantaneous RAM operations.

Then improve the scheduling model as you develop the GSTMCU and memory subsystem.

I would **not** hard-code an exact number of cycles for every rectangle based only on width and height. The cost depends on how many reads and writes are needed, the selected operation, masks, skew, bus mode, and competing memory activity. Published execution-time analysis explicitly describes these access-dependent costs. 

##### The mental model to keep

The Atari STE Blitter is a **programmable 16-bit memory-to-memory processing engine**. The CPU tells it what operation to perform; the Blitter generates addresses, fetches words, shifts and combines bits, applies masks, and writes results. The GSTMCU and shared memory system determine when those accesses can be serviced.

That makes it a graphics accelerator in common use, but architecturally it is more general than a sprite engine. Treating it as a bus-mastering memory processor will give your emulator the right foundation.

---

## Turn 31 — 2026-10-28 21:52 UTC

### Question

> what are the most accurate, up to date, and well architected atari st emulators

### Answer

For your project, I would evaluate Atari ST emulators on three separate axes: **hardware accuracy, quality of the internal architecture, and how actively the codebase is maintained**. Those are not the same thing. An emulator can run nearly every game while still using a simplified video model; another can have excellent hardware abstractions but lack some peripheral or compatibility features.

For your specific target — a PAL Atari STE, 68000, 4 MB RAM, and TOS 1.62 — I'd prioritize emulators that model the **GSTMCU, Shifter, Blitter, DMA sound, MFP interrupts, and shared-memory timing** rather than just focusing on software compatibility.

#### My shortlist, ranked for your project

<box gap={4}>
  <row align="start" gap={3}>
    <AsyncImage query="Hatari Atari ST emulator screenshot desktop debugger" aspectRatio="4:3" maxWidth="124px" />
    <box flex="1" gap={1}>
      <title size="lg">1. <Entity value="Hatari" category="software" disambig="Atari ST, STE, TT and Falcon emulator" /></title>
      <badge color="success">Best overall reference</badge>

      **Best for:** established hardware behavior, compatibility testing, debugging, and comparing your emulator against a mature implementation.

      Hatari supports the ST, STE, Mega ST, TT, and Falcon, with cycle-accurate 68000 options, raster effects, Blitter emulation, and a broad set of peripherals. It also has a documented software-compatibility list. Version 2.6.1 was released in August 2025, and its development repository showed activity in October 2026. 

      **Why I'd choose it:** it is the strongest starting point for validating your target's visible behavior and many of its timing-sensitive effects.

      Links: [Official site](https://www.hatari-emu.org/) · [Source code](https://framagit.org/hatari/hatari) · [Compatibility list](https://www.hatari-emu.org/doc/compatibility.html)
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="NeoST Atari ST emulator web browser debugger screenshot" aspectRatio="4:3" maxWidth="124px" />
    <box flex="1" gap={1}>
      <title size="lg">2. <Entity value="NeoST" category="software" disambig="Open-source Atari ST and STE emulator" /></title>
      <badge>Most interesting architecture for learning</badge>

      **Best for:** understanding the machine as a collection of separate chips connected by a bus, and developing your own emulator from a readable model.

      NeoST explicitly models components such as the Shifter, YM2149, MFP, WD1772, MMU/GLUE, and Blitter separately. It uses the Moira 68000 core, has a debugger and headless testing tools, and compares output against Hatari. Its own documentation is unusually candid about remaining cycle-accuracy work and known divergences. 

      **The caveat:** it is a much newer project, so don't mistake a clean architecture or passing tests for proof of complete hardware accuracy. Treat it as an excellent learning codebase and a source of ideas, while checking behavior against Hatari, MAME, documentation, and hardware tests.

      Link: [GitHub repository and documentation](https://github.com/habib256/neost)
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Steem SSE Atari ST emulator Windows debugger screenshot" aspectRatio="4:3" maxWidth="124px" />
    <box flex="1" gap={1}>
      <title size="lg">3. <Entity value="Steem SSE" category="software" disambig="Atari ST and STE emulator" /></title>
      <badge>Strong alternative for ST/STE demos</badge>

      **Best for:** a second comparison target, particularly for timing-sensitive demos and classic ST/STE software.

      Steem SSE extends the original Steem codebase with accuracy improvements, including Shifter/MMU behavior, border effects, Blitter support, and support for protected disk images. It remains actively developed; the project published 4.2.1 builds in 2026 and a 4.2.2 beta in October. 

      **The caveat:** it inherits an older codebase and is not my first choice for studying a clean, modern chip-by-chip architecture. Its ongoing maintenance is a plus, but activity alone doesn't establish that it is more accurate than Hatari.

      Link: [SourceForge project](https://sourceforge.net/projects/steemsse/)
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="MAME emulator Atari ST computer emulation screenshot" aspectRatio="4:3" maxWidth="124px" />
    <box flex="1" gap={1}>
      <title size="lg">4. <Entity value="MAME" category="software" disambig="Multi-system hardware emulator" /></title>
      <badge>Useful hardware-model reference</badge>

      **Best for:** studying how hardware devices are separated and integrated within a broader emulation framework.

      MAME has distinct Atari ST video, MMU, keyboard, CPU, and peripheral components. Its video source explicitly describes the collaboration between GLUE, MMU, and Shifter, and its machine driver maps the Blitter and other devices into the system address space. 

      **The caveat:** its Atari ST driver has documented unfinished areas, including floppy writing and floppy DMA-transfer timing in the source version inspected. That makes it a valuable reference, but not an automatic accuracy winner for every STE subsystem. 

      Link: [Atari ST machine driver](https://github.com/mamedev/mame/blob/master/src/mame/atari/atarist.cpp) · [Video model](https://github.com/mamedev/mame/blob/master/src/mame/atari/stvideo.cpp)
    </box>
  </row>
</box>

#### Quick comparison

| Emulator | Hardware accuracy | Architecture for study | Maturity | Best role |
|---|---|---|---|---|
| **Hatari** | Strong overall; cycle-accurate CPU options and timing-sensitive video | Mature, established codebase | Very high | Main reference and regression oracle |
| **NeoST** | Promising; actively documenting remaining inaccuracies | Very transparent, chip-oriented design | Early-stage | Learning and architecture inspiration |
| **Steem SSE** | Strong for many ST/STE timing effects | Older codebase with years of refinements | Actively maintained | Independent comparison, especially demos |
| **MAME** | Detailed device models, but subsystem completeness varies | Device-oriented and well integrated into a general framework | Very high as a project | Hardware modeling and cross-checking |

These are qualitative assessments, not results from a single standardized hardware-accuracy benchmark. There is no universally accepted ranking that proves one emulator is the most accurate in every ST/STE subsystem.

#### What I would actually do in your position

Use **Hatari as your primary reference, NeoST as your architecture study, and MAME as a secondary technical reference**. Keep Steem SSE available for disagreements involving timing-sensitive ST/STE demos.

For your particular STE, I would compare the following areas separately rather than asking whether an emulator is simply “cycle accurate”:

- **CPU and bus:** instruction timing, bus errors, interrupt sampling, and CPU stalls.
- **Video:** raster timing, palette writes, fine scrolling, border effects, and video-memory fetches.
- **Blitter:** register behavior, masks, skew, bus-hog mode, cooperative mode, and actual completion timing.
- **Sound:** DMA sample rates, memory contention, and synchronization with video.
- **System coordination:** MFP timers, HBL/VBL timing, DMA transfers, and interactions among simultaneous devices.

A useful test workflow is to run identical software in Hatari and NeoST, capture frames and traces, and investigate each divergence against the original hardware documentation. NeoST already describes this kind of differential comparison in its documentation. 

One caution: matching Hatari is evidence of compatibility with Hatari, not proof of matching a physical Atari STE. When both emulators share assumptions or bugs, you need a third source of truth—ideally a hardware test, a reliable schematic, or a documented behavior verified on real hardware.

**My bottom line:** if you want the safest existing emulator, start with **Hatari**. If you want the codebase whose architecture most closely matches the chip-by-chip learning approach we've been using, investigate **NeoST** first—but use it as a learning project, not yet as your sole hardware specification.

---

## Turn 32 — 2026-10-28 21:58 UTC

### Question

> compare the language and toolchain used for the first 3, study their level of simulation/émulation, compare their approaches and architectures

### Answer

### 1. The biggest distinction: all three are software emulators, not silicon simulators

First, it helps to separate three levels of modeling. These terms aren't used consistently across emulator projects, so I'll use them in a specific sense here.

<box border radius="xl" padding={3} gap={3}>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} size="44px" align="center" justify="center">
      <icon name="cpu" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **Level 1 — Functional emulation**

      The device produces the right results for software-visible operations: a Blitter copy works, a PSG register changes sound, and a CPU instruction updates registers correctly. Timing may be approximate.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} size="44px" align="center" justify="center">
      <icon name="timer" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **Level 2 — Cycle- and event-aware emulation**

      The emulator tracks when operations happen, schedules interrupts, models bus stalls and register-write timing, and coordinates the CPU with video and peripherals. It can reproduce many timing-sensitive demos and software tricks.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} size="44px" align="center" justify="center">
      <icon name="circuit-board" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **Level 3 — Gate-level or electrical simulation**

      Logic gates, flip-flops, propagation delays, pin-level signal interactions, or transistor-level behavior are modeled explicitly. A simulation of reconstructed GSTMCU logic in Verilog would be an example of this style, depending on its level of detail.
    </box>
  </row>
</box>

**Hatari, NeoST, and Steem SSE are all software emulators, not full electrical simulations of the Atari motherboard.** They model devices at a behavioral and timing level. A cycle-exact CPU core does not make the entire computer cycle-exact: video fetches, Blitter bus ownership, interrupt latency, and peripheral timing all have to agree with the CPU's timeline.

That distinction is especially important for your project, because we have been studying the STE as interacting hardware subsystems, not just a 68000 running instructions.

#### 2. Language and build toolchain

The three projects make different engineering trade-offs before we even get to emulation accuracy.

| Aspect | Hatari | NeoST | Steem SSE |
|---|---|---|---|
| Main language | C | C++17 | C++ |
| CPU core | WinUAE-derived 68000 core | Vendored Moira core | Its own 68000 emulation implementation |
| Build system | CMake | CMake | Legacy project/build scripts; multiple compiler/build configurations |
| Graphics/window layer | SDL | GLFW and OpenGL | Platform-specific rendering; Direct3D on Windows, with other platform paths |
| Sound output | SDL and supporting audio paths | miniaudio | Platform/audio backends |
| Typical developer tools | GCC or Clang, CMake, debugger and test scripts | C++ compiler, CMake, headless tests, trace tools | C++ compiler, platform debugger, debug builds and trace output |
| Main engineering emphasis | Mature compatibility and timing | Explicit chip-oriented structure and experimental verification | Mature ST/STE behavior and iterative compatibility improvements |

Sources: Hatari's build instructions and source layout, NeoST's repository and development documentation, and Steem SSE's source-structure and release notes. 

A couple of details deserve clarification.

- **Hatari is primarily C**, not C++. It uses CMake and SDL, and its CPU implementation is derived from the WinUAE core. Its source tree separates CPU, video, debugging, and platform-facing code.
- **NeoST is C++17**, not Rust. It uses Moira for the 68000, CMake for builds, GLFW/OpenGL for the desktop presentation, and miniaudio for audio. Its repository includes a headless testing workflow and detailed development notes.
- **Steem SSE is C++**, descended from the original Steem project. Its source history includes a distinctive single-compilation-unit approach, later adjusted to support different build configurations. Its toolchain is more historically evolved than NeoST's deliberately organized modern CMake setup. 

##### What these choices mean for you

C and C++ are both suitable for a hardware emulator. Neither language inherently produces better cycle accuracy.

C can make low-level state and memory operations straightforward, and it is the language of Hatari's established implementation. C++ makes it natural to represent chips as objects with explicit state and interfaces, as NeoST does. But C++ abstractions only help if they preserve the timing and shared-state relationships of the real hardware.

For a new emulator, I would choose C++ if you prefer explicit device classes, typed registers, and well-defined interfaces. I would not choose it *because* it is more accurate; accuracy comes from the model and its validation.

#### 3. Architecture: how each emulator organizes the machine

##### Hatari — a mature, integrated emulator

Hatari's architecture has grown around a practical objective: reproduce enough real ST/STE behavior to run ordinary software, games, and difficult demos reliably. Its modules cover the CPU, video, sound, MFP, IKBD, floppy controller, Blitter, storage, and other devices. The modules are distinct, but they are integrated through shared timing, memory access, and scheduling logic. 

<box border radius="xl" padding={3} gap={2}>
  <box background="surface-secondary" radius="md" padding={3} align="center">
    **Main emulation loop**

    <text size="sm">CPU execution and global time progression</text>
  </box>
  <box align="center">
    <icon name="arrow-down-up" size="lg" />
  </box>
  <box border radius="md" padding={3} align="center">
    **Timing and event scheduler**

    <text size="sm" textAlign="center">Video events, MFP timers, interrupts, disk events, Blitter events</text>
  </box>
  <grid columns={3} gap={2}>
    <grid-item>
      <box background="surface" border radius="md" padding={2} align="center">
        <text weight="medium" textAlign="center">CPU core</text>
        <text size="xs" textAlign="center">68000 timing</text>
      </box>
    </grid-item>
    <grid-item>
      <box background="surface" border radius="md" padding={2} align="center">
        <text weight="medium" textAlign="center">Video</text>
        <text size="xs" textAlign="center">Shifter / GLUE</text>
      </box>
    </grid-item>
    <grid-item>
      <box background="surface" border radius="md" padding={2} align="center">
        <text weight="medium" textAlign="center">Peripherals</text>
        <text size="xs" textAlign="center">MFP / FDC / PSG</text>
      </box>
    </grid-item>
  </grid>
  <text color="secondary" size="xs">Conceptual view, not a literal diagram of Hatari's source call graph.</text>
</box>

**Strengths**

- A great deal of accumulated knowledge about actual ST/STE compatibility.
- A mature CPU core with cycle-accurate operation available.
- A broad set of timing-sensitive video effects and peripheral behaviors.
- A practical debugging and tracing environment.
- A strong reference point for comparing your own results.

**Architectural trade-off**

Some timing behavior is implemented in device modules, while some of the coordination lives in shared timing and event code. To understand a behavior completely, you may need to follow it across multiple source files. That is normal for a mature emulator, but it can make one isolated device harder to understand than its interface suggests.

**How I would study it:** start with the CPU timing path, the event scheduler, and the video timing code. Then trace one concrete event—such as an MFP Timer B event triggered by the end of display enable—from its source to the interrupt delivered to the CPU.

##### NeoST — an explicit, chip-oriented design

NeoST is designed to make the emulated hardware visible to developers. Its README describes separate models for the Shifter, YM2149, MFP, WD1772, MMU/GLUE, and Blitter, connected through a `Bus` representing the system's memory map. Its development documentation also describes a clock model, event scheduler, headless operation, and differential tests. 

<box border radius="xl" padding={3} gap={2}>
  <box background="surface-secondary" radius="md" padding={3} align="center">
    **Machine / clock scheduler**
  </box>
  <box align="center">
    <icon name="arrow-down" size="lg" />
  </box>
  <box border radius="md" padding={3} align="center">
    **System Bus**

    <text size="sm" textAlign="center">Memory map and device access</text>
  </box>
  <grid columns={2} gap={2}>
    {#each ["CPU / Moira","MMU / GLUE","Shifter","Blitter","MFP / ACIA","PSG / DMA sound","WD1772 / DMA","RAM / ROM"] as part}
      <grid-item>
        <box background="surface" border radius="md" padding={2}>
          <text size="sm" textAlign="center">{part}</text>
        </box>
      </grid-item>
    {/each}
  </grid>
  <text color="secondary" size="xs">Conceptual view of the chip-oriented organization.</text>
</box>

The advantage is that each chip's state and responsibilities are easier to locate. If you are learning the Blitter, you can investigate its register interface, state machine, and bus interactions as a distinct unit rather than treating it as part of a larger rendering or CPU subsystem.

But the important architectural question is not merely whether there is a class named `Blitter`. It is **how that class interacts with the scheduler, CPU, and memory bus**. A nicely separated device that performs an entire operation instantly would be less faithful than a tightly integrated implementation that schedules each important memory access correctly.

NeoST's documentation explicitly tracks cycle-accuracy gaps and tests against Hatari. Its cycle-accuracy notes describe a scheduler with dated hardware events, a global clock, cycle-aware video and MFP behavior, and detailed Blitter bus-sharing work. They also identify remaining work, including timing comparisons and some STE-specific cases. 

**Architectural strengths**

- Very approachable device boundaries.
- Strong developer documentation explaining what is implemented and what remains.
- Headless execution and trace-diff tooling suited to repeatable tests.
- An explicit goal of documenting divergences instead of hiding them.

**Trade-off**

It is a younger emulator and much of its behavior is ported or adapted from Hatari. Its clean structure does not automatically make it more accurate, and agreement with Hatari cannot validate behavior where Hatari itself is wrong. NeoST's own documentation acknowledges this and treats hardware documentation and measurements as the ultimate sources of truth. 

##### Steem SSE — compatibility refined over a long development history

Steem SSE derives from the original Steem C++ emulator and has accumulated many years of ST/STE-specific corrections. Its developer documents a source structure that evolved between large amalgamated compilation units and separate C++ compilation units, partly to balance build time and optimization. 

Its development history illustrates a different route to accuracy: improve a working emulator in response to concrete failures, timing-sensitive demos, hardware documentation, and tests. Its changelog includes improvements to Blitter NFSR behavior, MFP timing, CPU interrupt timing, video counters, and STE audio. 

**Strengths**

- Deep experience with real ST/STE software compatibility.
- Many fixes for difficult display effects and peripheral timing.
- A debugger-oriented build and useful tracing facilities.
- A long history of tuning behavior against specific programs and hardware quirks.

**Trade-off**

The code reflects its evolution. It may be harder to derive a single clean architectural explanation from the source because the implementation has accumulated optimizations, compatibility choices, and historical design decisions. The developer's own discussion of source organization is evidence of that evolution—not proof that the resulting architecture is poor. 

**How I would study it:** use it to investigate concrete edge cases, particularly those that expose the difference between a simplified functional model and actual ST/STE timing. It is less attractive than NeoST as a first codebase for learning the machine's organization from scratch.

#### 4. Compare their *level of emulation*, not just their reputations

This is where a simple ranking becomes misleading. A machine has many independently difficult timing problems, and an emulator can be strong in one while approximating another.

| Subsystem | Hatari | NeoST | Steem SSE |
|---|---|---|---|
| **68000 CPU** | Mature WinUAE-derived core; cycle-accurate option | Moira core with precise timing enabled in its documented accuracy model | Long-developed CPU model with timing and interrupt refinements |
| **Memory map and device registers** | Mature and broadly tested | Explicit Bus and device register models | Mature, integrated register and memory models |
| **Video / raster effects** | Strong, widely used reference | Detailed scanline/event model; compared against Hatari | Strong history of ST/STE demo and border-effect fixes |
| **Blitter** | Supported and integrated into event/timing system | Explicit device with documented bus-sharing work; some timing validation still open | Supported, with iterative fixes for edge cases |
| **MFP / interrupts** | Mature event-driven implementation | Dated timer and IRQ model with documented remaining comparisons | Detailed timing refinements, including interrupt sampling behavior |
| **Floppy / DMA** | Broad support, including several disk image formats and protected-image paths | Explicit WD1772 and DMA models; temporal FDC model under active refinement | Strong disk-image compatibility and ongoing timing improvements |
| **Sound** | Mature PSG and STE sound support | Explicit PSG and DMA sound devices; timing refinements ongoing | Mature PSG and STE sound support with historical fixes |
| **Whole-machine timing** | Strong overall, but not every bus interaction is modeled electrically | Explicit cycle/event framework; known divergences documented | Strong practical compatibility, with some behavior dependent on options and historical implementation choices |

This is a qualitative comparison based on project documentation and changelogs, not a claim that every row has been benchmarked on identical physical hardware. 

##### The subtle but important difference between CPU accuracy and system accuracy

A cycle-accurate CPU core can tell the emulator how long an instruction takes and when its bus accesses occur. It cannot by itself determine whether:

- the Shifter has priority over a simultaneous Blitter request;
- a CPU write to a video register takes effect immediately or at a later fetch boundary;
- an MFP timer edge occurs before or after a video event;
- a DMA sound fetch delays a CPU memory access;
- a Blitter operation is paused halfway through a word when the CPU regains the bus.

Those behaviors belong to the whole-machine model.

NeoST's current cycle-accuracy documentation is particularly useful here because it distinguishes what has been implemented from what remains to be qualified. It describes a dated event scheduler, cycle-aware video and interrupt behavior, and detailed Blitter bus-sharing work, while also listing remaining timing investigations. It explicitly says that some bus-contention effects are not modeled because the current Hatari comparison model does not model them either. 

That is a crucial lesson for your emulator: **reproducing the reference emulator's behavior and reproducing the physical machine's behavior are two different objectives**.

#### 5. What their testing approaches tell us

<box gap={3}>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="lg" padding={2} size="44px" align="center" justify="center">
      <icon name="check-circle-2" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **Hatari — regression and compatibility**

      Hatari has a mature test suite, diagnostic and debugger facilities, and a long history of supporting real games and demos. It is useful as a baseline for whether your implementation behaves like an established emulator. Its age and maturity are strengths, but you should still verify disputed hardware details independently. 
    </box>
  </row>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="lg" padding={2} size="44px" align="center" justify="center">
      <icon name="git-compare" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **NeoST — explicit differential validation**

      NeoST documents headless runs, CPU/register traces, pixel-by-pixel frame comparisons, and a divergence catalogue. This makes its development process especially relevant to a new emulator: a mismatch can be recorded, reproduced, and tracked instead of being fixed only by visual intuition. 
    </box>
  </row>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="lg" padding={2} size="44px" align="center" justify="center">
      <icon name="wrench" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **Steem SSE — iterative hardware-compatibility fixes**

      Its release history documents individual improvements tied to real failures: Blitter source-read behavior, CPU interrupt sampling, video-counter writes, MFP timer behavior, and sound timing. That is useful evidence of the kind of edge cases that matter, though a changelog alone cannot quantify overall accuracy. 
    </box>
  </row>
</box>

For your emulator, I would combine these approaches: build a clean, explicit architecture; run deterministic tests headlessly; compare against more than one emulator; and keep a written list of unresolved hardware questions.

#### 6. Which architecture would I copy for your Atari STE?

My recommendation is to combine the best aspects of all three, rather than clone any one codebase.

<box border radius="xl" padding={3} gap={3}>
  <title size="lg">Recommended design for your emulator</title>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} size="40px" align="center" justify="center">
      <icon name="blocks" size="lg" />
    </box>
    <box flex="1" gap={1}>
      **NeoST's explicit chip boundaries**

      Give the 68000, GSTMCU, Shifter, Blitter, MFP, PSG, DMA sound, WD1772, DMA controller, and IKBD their own state and well-defined interfaces.
    </box>
  </row>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} size="40px" align="center" justify="center">
      <icon name="clock-3" size="lg" />
    </box>
    <box flex="1" gap={1}>
      **Hatari's mature timing model**

      Study its CPU-cycle accounting, scheduled events, video timing, and peripheral synchronization. Reuse ideas only after understanding their assumptions.
    </box>
  </row>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} size="40px" align="center" justify="center">
      <icon name="test-tube" size="lg" />
    </box>
    <box flex="1" gap={1}>
      **Steem SSE's attention to edge cases**

      Build a library of small programs that isolate obscure behavior: Blitter skew, mid-line palette changes, counter writes, MFP event counting, and interrupt timing.
    </box>
  </row>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" padding={2} size="40px" align="center" justify="center">
      <icon name="git-compare" size="lg" />
    </box>
    <box flex="1" gap={1}>
      **NeoST's differential workflow**

      Make every bug report reproducible with a test program, a configuration, a trace, expected results, and an explanation of whether the expected behavior is hardware-verified or merely emulator-compatible.
    </box>
  </row>
</box>

I would also introduce a clean distinction between **device state, bus transactions, and timed events**. For example, the Blitter should calculate what memory access it needs; the bus/memory system should decide when that access is serviced; and the scheduler should arrange the next event based on the actual timing. That architecture is easier to refine than a Blitter that directly reads and writes RAM and then adds an estimated delay.

##### Final verdict

- **Best mature reference:** Hatari.
- **Best architecture to study as a learner and emulator developer:** NeoST.
- **Best complementary source of long-refined ST/STE corner cases:** Steem SSE.

For the machine we've been studying, I'd start by reading NeoST's `DEV.md` and `docs/CYCLE_ACCURACY.md`, then trace the equivalent behavior through Hatari. Keep Steem SSE as a third comparison point when their behavior differs.

The most important principle is this: **a readable architecture helps you understand and improve accuracy, but only a carefully validated timing model makes it accurate.**

---

## Turn 33 — 2026-10-28 22:18 UTC

### Question

> in real hardware. many components execute independently of each other in parallel, and synchronize through signals only when they need to exchange. some components execute synchrously, driven by the same master clock in hardware. often, in an emulator it is not practical to execute logic for each master clock half cycle, because it would be too slow. thus, the key architectural decision is how you model shared elapsed time vs shared clock, vs sync signals in the emulator. can you study in great détails the choices of the theee emulators above.

### Answer

### Atari ST/STE emulation: shared time, clocks, and synchronization

Your observation gets to the heart of accurate computer emulation. The hardest architectural question is not simply how to emulate each chip. It is **how to preserve the relationships between independently operating chips without simulating every electrical transition individually**.

For our PAL Atari STE, this distinction matters because the 68000, GSTMCU, Shifter, Blitter, DMA sound, floppy DMA, and MFP do not all operate in the same way:

- Some state changes are driven by a periodic clock.
- Some operations take a known number of clock periods but can be advanced in batches.
- Some devices operate autonomously until they need a resource or encounter a synchronization event.
- Some signals cross between timing domains.
- Some operations compete for shared resources, especially RAM and bus access.
- Some effects depend on the *exact ordering* of two events that happen at the same nominal time.

A useful emulator architecture needs to represent all of these without necessarily evaluating every master-clock edge.

#### 1. Four different concepts that should not be conflated

<box border radius="xl" padding={3} gap={3}>
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="44px" align="center" justify="center">
      <icon name="clock" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **A. Shared elapsed time**

      The emulator has a common notion of when something happened. A CPU instruction might begin at time \(t\) and complete at \(t+\Delta t\). Other devices can advance to that same point, even if they use different internal clocks.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="44px" align="center" justify="center">
      <icon name="activity" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **B. Shared clock phase**

      Two components can use the same clock or derived clocks with a known phase relationship. Knowing that two events happen at cycle 100 is not always enough: one may occur on a rising edge, another on a falling edge, and the order can affect what each component observes.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="44px" align="center" justify="center">
      <icon name="git-merge" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **C. Signal synchronization**

      A device may react to a rising edge, falling edge, level, or handshake. It does not necessarily need to execute in lockstep with the component producing that signal. The receiver may sample the signal on its own clock.
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <box background="surface-secondary" radius="md" size="44px" align="center" justify="center">
      <icon name="arrow-left-right" size="xl" />
    </box>
    <box flex="1" gap={1}>
      **D. Shared-resource arbitration**

      Independent devices can be ready simultaneously but unable to access RAM simultaneously. The emulator must decide who gets service, which requests wait, and what other components do during that wait.
    </box>
  </row>
</box>

These are four distinct requirements. An emulator can implement shared elapsed time without modeling clock phase, and it can model signal edges without simulating every gate. Likewise, it can model bus arbitration at word-access granularity without reproducing every DRAM pin transition.

For the STE, I would treat them as separate architectural capabilities, then connect them through a common timing model.

#### 2. The three emulators' actual time models

The key finding from the source and developer documentation is that these projects do **not** run every chip in a separate operating-system thread, nor do they simulate the whole board at every 32 MHz master-clock edge. Instead, they combine a CPU execution engine with scheduled hardware events and device-specific timing models. They differ in how far they push cycle-level synchronization and how their code represents it.

#### A. Hatari: a CPU-cycle timebase plus a scheduled-event system

Hatari has an explicit global cycle counter, `CyclesGlobalClockCounter`, and a central event system in `cycInt.c` / `cycInt.h`. Each event source—video HBL/VBL/end-of-line, MFP timers, ACIA, FDC, Blitter, DMA sound, and others—can schedule a future event. The event scheduler tracks the next deadline rather than decrementing every timer independently on every simulated clock. 

<box border radius="xl" padding={3} gap={2}>
  <box background="surface-secondary" radius="md" padding={3} align="center">
    **68000 execution**

    <text size="sm" textAlign="center">Instruction execution produces cycle costs and timed bus accesses.</text>
  </box>
  <box align="center">
    <icon name="arrow-down-up" size="lg" />
  </box>
  <box border radius="md" padding={3} align="center">
    **Global emulated cycle counter**

    <text size="sm" textAlign="center">Common reference for CPU-visible time and hardware timing.</text>
  </box>
  <box align="center">
    <icon name="arrow-down-up" size="lg" />
  </box>
  <box border radius="md" padding={3} align="center">
    **Central scheduled-event queue**

    <text size="sm" textAlign="center">Next deadline → invoke device handler → update or schedule subsequent events.</text>
  </box>
  <grid columns={3} gap={2}>
    <grid-item>
      <box background="surface" border radius="md" padding={2} align="center">
        <text textAlign="center" size="sm">Video</text>
      </box>
    </grid-item>
    <grid-item>
      <box background="surface" border radius="md" padding={2} align="center">
        <text textAlign="center" size="sm">MFP</text>
      </box>
    </grid-item>
    <grid-item>
      <box background="surface" border radius="md" padding={2} align="center">
        <text textAlign="center" size="sm">FDC / Blitter / sound</text>
      </box>
    </grid-item>
  </grid>
  <text color="secondary" size="xs">Conceptual view of Hatari's timing architecture, not a literal depiction of every source-code call.</text>
</box>

##### How it avoids per-clock execution

Suppose an MFP timer will expire 73 CPU-cycle units from now. Hatari can record that deadline. It does not need to run the timer's logic 73 times to reach the event.

As the CPU advances, the scheduler tracks how far it has progressed toward that deadline. When the deadline is reached or crossed, the event handler runs. If a long instruction crosses the deadline, the implementation has machinery for accounting for the overrun instead of permanently shifting the timer's phase. Hatari supports both relative scheduling and absolute scheduling, the latter being useful for repeating events without accumulated drift. 

The important distinction is that **the event queue represents future state transitions, not the individual clock edges that would cause them in the physical chip**.

##### How it represents different clock domains

Hatari has explicit machine clock tables. For the STE, the documented nominal relationships include:

- 32 MHz master clock.
- 16 MHz GST Shifter clock.
- 8 MHz CPU/bus and Blitter clock.
- 8 MHz sound-DMA clock from the separate sound oscillator.
- 4 MHz MFP clock and 500 kHz ACIA clock derived from the GSTMCU.
- 2 MHz YM2149 clock.

The MFP also has its own 2.4576 MHz external timer clock. These are not all the same clock domain. 

Hatari represents the relationships through integer counters, clock-conversion functions, and scaled internal time units. The scheduler's clock types distinguish CPU-cycle, MFP-cycle, and other machine-cycle units; the conversion macros in `cycInt.h` map them into a common internal timeline. This is how it avoids treating a 2.4576 MHz timer as if it simply advanced at CPU frequency. 

This is not the same as simulating all clock wires. It preserves the relevant timing relationships mathematically.

##### How signals are represented

A device event invokes a handler which updates the relevant device state and may request an interrupt or schedule another event. For example, the MFP code records when an interrupt request becomes pending and accounts for a documented delay before the CPU can observe it. The MFP is not executed concurrently with the CPU in a separate thread; its behavior is integrated with CPU timing and interrupt processing. 

This is a pragmatic hybrid:

- **Clock-driven behavior** is represented by cycle counts and scheduled deadlines.
- **Signal effects** are represented by state changes and event handlers.
- **CPU-visible timing** is reconciled with the CPU execution engine.
- **Video timing** has its own detailed beam/scanline model.

##### The trade-off

Hatari's architecture is efficient and mature. But the event queue does not, by itself, make every device transition accurate. Accuracy depends on where events are scheduled, whether a deadline can be observed during an instruction, and how the handler models the hardware.

Its own documentation warns that CPU and audio clocks are not always exact multiples of one another; rounding the nominal frame rate can cause synchronization errors between video and STE DMA audio. 

That is an important example of the problem you identified: **a common clock counter is useful only if its conversions retain the real phase relationship between the clock domains**.

#### B. NeoST: a continuous event timeline, explicit scheduler, and cycle-exact CPU core

NeoST's architecture is particularly relevant to your question because its developer documentation explicitly describes its clock model and the remaining work needed for greater timing accuracy.

The `Machine` assembles individual chip models; the `Bus` routes memory-mapped accesses; and `Scheduler` stores dated events. `Machine::runFrame()` advances a continuous emulated timeline, carrying excess time across frame boundaries rather than resetting the clock to zero on each frame. The CPU core is Moira in cycle-exact mode. 

<box border radius="xl" padding={3} gap={2}>
  <box background="surface-secondary" radius="md" padding={3} align="center">
    **Machine**

    <text size="sm" textAlign="center">Owns device state and the continuous emulation timeline.</text>
  </box>
  <grid columns={2} gap={2}>
    <grid-item>
      <box border radius="md" padding={3} align="center">
        **Moira / CPU**

        <text size="sm" textAlign="center">Cycle-exact instruction execution and timed bus accesses</text>
      </box>
    </grid-item>
    <grid-item>
      <box border radius="md" padding={3} align="center">
        **Scheduler**

        <text size="sm" textAlign="center">Dated events, deadlines, and callbacks</text>
      </box>
    </grid-item>
  </grid>
  <box align="center">
    <icon name="arrow-down-up" size="lg" />
  </box>
  <box border radius="md" padding={3} align="center">
    **Bus and device models**

    <text size="sm" textAlign="center">Memory-mapped I/O, register effects, video events, Blitter, MFP, and FDC timing</text>
  </box>
  <text color="secondary" size="xs">Conceptual view based on the documented source organization.</text>
</box>

##### The CPU is not the global clock

This is an important design choice. Moira determines the detailed execution of the 68000, but the rest of the machine is not simply updated once after each instruction.

NeoST's documented timing model uses:

- A continuous cycle timeline.
- Scheduled device events with absolute deadlines.
- A cycle-exact CPU core.
- A scheduler-aware mechanism for determining the current time during an instruction.
- Device-specific models for video timing, MFP timers, disk operations, and Blitter progress.

The developer guide calls out `Scheduler::liveNow()` and CPU timeslice preemption. The purpose is to expose the relevant current time when an operation occurs inside a CPU instruction, rather than forcing every register write to appear to happen only at an instruction boundary. 

This is an important improvement over a simplistic loop such as:

```text
execute_one_instruction()
advance_every_device(instruction_cycles)
```

That simple loop can only deliver events accurately at the points where it updates the devices, unless the CPU core or bus layer exposes finer-grained timing.

##### Its handling of clock domains

NeoST's MFP model is a good concrete example. The MFP's timer frequency is not treated as identical to the CPU clock. The project documents integer fixed-point conversion between CPU and MFP cycles, retaining fractional timing when scheduling timer deadlines and carrying that fractional phase forward when timers are reloaded. 

The design principle is sound: **represent the clock-domain relationship exactly enough that repeated conversions do not accumulate rounding drift**.

For a PAL STE, you can apply the same approach to the independent 8 MHz sound clock and the 8 MHz CPU/bus clock. Although their nominal frequencies are equal, their relationship to the master oscillator and their phase alignment are separate questions. Equal frequency does not automatically mean identical clock edges.

##### What NeoST does with video synchronization

NeoST schedules video events at particular cycle positions: display-enable transitions, end-of-line, HBL, and VBL. Its Shifter renders scanlines using the state relevant to the line, while register changes and video-counter behavior are assigned to defined timing points. 

However, the documentation also marks fine beam synchronization as ongoing work. Its remaining accuracy list includes the timing of Blitter activity alongside other unresolved details. So the architecture should not be interpreted as a complete simulation of every GSTMCU/Shifter signal edge. 

##### What it does not simulate

NeoST does not simulate all the chip pins, every rising/falling edge of the 32 MHz clock, or the internal gates of the GSTMCU. Instead, it uses an explicit, continuous timeline and device-level timing models.

Its key advantage over a less structured emulator is not that it executes all hardware in lockstep. It is that the timeline, device responsibilities, and scheduled interactions are easier to inspect, test, and refine.

##### The trade-off

NeoST's model is particularly readable and testable, but there is a subtle risk: an event can be scheduled at an exact cycle and still represent the wrong physical event. A model can be precise about the wrong abstraction.

The project's own methodology acknowledges this. It uses Hatari as a comparison oracle, but it also documents cases where Hatari is believed to be wrong and where hardware documentation or measurements must take precedence. 

That is the right philosophy for your emulator, too.

#### C. Steem SSE: cycle-sensitive CPU behavior, practical timing models, and compatibility fixes

Steem SSE has evolved through a different route. Its timing model has accumulated corrections for particular hardware interactions and demos, including a revised 68000 interrupt model, Blitter timing, video-counter behavior, MFP timers, and disk-transfer timing. Its release notes document these changes in terms of observable hardware/software failures. 

I want to be precise about the evidence here: Steem SSE's public developer documentation is not as complete a single architectural specification as NeoST's `DEV.md` and `CYCLE_ACCURACY.md`. The description below therefore distinguishes documented implementation decisions from conclusions inferred from the fixes and options.

##### A CPU execution model with timing refined around real behavior

One major change in Steem SSE 4.0 was its interrupt model. Earlier behavior checked for interrupts between instructions. The revised model accounts for the 68000's interrupt sampling during instruction execution, using knowledge of where the CPU makes its interrupt decision. The developer describes this as a substantial change that also shifted MFP, HBL, and VBL timing. 

This is a good example of why CPU execution and peripheral timing cannot be completely separate. A peripheral can raise an interrupt at the correct emulated time, but if the CPU samples the interrupt at the wrong point, software still observes incorrect behavior.

##### Timing models can be optional or approximate

Steem SSE also exposes a “fast Blitter” option that skips counting the Blitter's timing cycles. Its documentation explicitly identifies this as a performance hack. The regular Blitter model, meanwhile, has received fixes for the pre-start busy period, bus access during disk DMA, and resuming an interrupted line. 

This tells us something important about its architecture: **not every configuration promises the same temporal fidelity**. The emulator can offer a faster approximation for general software while retaining more detailed timing behavior for demos that depend on it.

That is a legitimate engineering trade-off, but for your project it means tests must record the selected configuration. A result obtained with fast Blitter timing disabled is not comparable to one obtained with that shortcut enabled.

##### Clock domains and external timing

Steem SSE's release notes describe improvements to the relationship between CPU timing and the MFP timer crystal, a separate 1 MHz IKBD crystal, and the timing of MIDI and floppy operations. They also acknowledge finite host-side real-time precision for some external I/O paths. 

These are two different categories of timing:

- **Emulated hardware time:** CPU cycles, video events, MFP counters, Blitter accesses, and disk-controller operations.
- **Host real time:** when the emulator renders a frame, sends MIDI to a host device, or produces audio samples.

A good emulator keeps these distinct. Host scheduling jitter should not alter the deterministic timing of the virtual 68000 and its peripherals, even though the host may affect when a frame is displayed or an audio buffer is delivered.

##### What can we conclude about Steem SSE's architecture?

The evidence points to a timing model that has been progressively refined around actual CPU, video, Blitter, and peripheral behavior. It is valuable for learning where a practical emulator needs exceptions and timing corrections.

What I would *not* conclude from the release notes alone is that Steem SSE uses exactly the same central event-queue architecture as Hatari or NeoST, or that it implements every clock domain with the same precision. Establishing that would require tracing the current source code's execution loop, timer scheduling, and device-update paths in detail.

That qualification matters: a shared set of timing fixes does not imply a shared timing architecture.

#### 3. Side-by-side: what each emulator actually synchronizes

The following table summarizes the central architectural differences.

| Design question | Hatari | NeoST | Steem SSE |
|---|---|---|---|
| Common time reference | Global cycle counter with scaled internal units | Continuous cycle timeline and dated scheduler | CPU-cycle timing with device-specific timing machinery |
| CPU advancement | CPU core reports cycle costs; cycle-exact mode provides finer bus timing | Moira runs in cycle-exact mode, with scheduler-aware time and preemption | CPU model refined to reproduce instruction-internal interrupt sampling |
| Future events | Central event scheduler, `cycInt` | Explicit `Scheduler` with dated callbacks | Timing behavior refined through its existing device and CPU architecture; exact equivalence to the other queues not established here |
| Independent clock domains | Machine clock tables and cycle conversions | Integer/fixed-point conversions and retained timer fractions | Clock relationships and timer behavior corrected in device models |
| Signal handling | Device callbacks, state changes, interrupt processing, video events | Device state and scheduler callbacks, explicit register/bus interfaces | Timing behavior integrated with CPU and device models, including compatibility fixes |
| Shared bus | Blitter and DMA timing modeled within the machine's timing framework | Explicit bus/device structure and Blitter cooperative/HOG model | Blitter bus-access behavior refined, including disk-DMA conflicts |
| Every master-clock edge? | No | No | No |
| Main strength | Mature, broadly validated integrated model | Explicit, inspectable timing architecture | Practical accuracy refined through long-term compatibility work |

The most directly documented references are Hatari's event API and clock definitions, NeoST's development and cycle-accuracy guides, and Steem SSE's timing-related release notes. 

The distinction between these approaches is not simply that one uses events and another uses clocks. **All three use abstractions to avoid simulating every electrical transition.** The differences are in how those abstractions are represented, how finely they coordinate CPU and device activity, and how accurately they model the physical cause of an event.

#### 4. A subtle point: an event queue is not the same as independent hardware execution

Consider a physical MFP timer running while the CPU executes a long instruction.

In hardware, the timer does not wait for the CPU to finish the instruction. It continues counting, may assert its interrupt request, and the CPU observes that request according to its own interrupt-sampling rules.

There are several possible emulator implementations:

<box border radius="xl" padding={3} gap={3}>
  <box gap={1}>
    <title size="md">Model 1: Update the timer only after each instruction</title>
    <text size="sm">Easy to implement, but events can be delayed until the instruction ends. It needs additional correction logic if the exact event time matters.</text>
  </box>
  <divider color="subtle" />
  <box gap={1}>
    <title size="md">Model 2: Schedule the timer's deadline</title>
    <text size="sm">The timer records a future deadline. The scheduler processes it when CPU execution reaches or crosses that deadline, accounting for any overrun. This avoids stepping the timer on every clock.</text>
  </box>
  <divider color="subtle" />
  <box gap={1}>
    <title size="md">Model 3: Advance CPU and timer to a shared fine-grained time point</title>
    <text size="sm">The CPU and timer can be synchronized at a finer granularity, such as an individual bus cycle or relevant CPU micro-operation. This provides more precise ordering but requires greater coordination.</text>
  </box>
</box>

Hatari's MFP code explicitly handles the case where multiple timer inputs occur during one CPU instruction and preserves their chronological timing for interrupt processing. NeoST's documentation describes a dated-event scheduler plus a cycle-exact CPU core. These are examples of the second and third approaches being combined rather than used exclusively. 

A scheduler does not need to execute the timer 500 times to advance it by 500 timer ticks. But it does need enough state to know the correct counter value and the precise time at which an event becomes visible.

#### 5. The real architectural challenge: ordering simultaneous events

Now consider a harder example on your STE.

At emulated time \(t\):

- The Shifter is scheduled to fetch video data.
- The Blitter requests a RAM read.
- The CPU attempts a memory write.
- The MFP Timer B input changes because display enable changes state.

A simplistic emulator may process those in whichever order its C++ or C functions happen to run. That order might not match the hardware.

A more accurate emulator needs an explicit rule for simultaneous events. The rule can depend on the hardware's clock phases, bus arbitration, and whether an operation samples a signal before or after a clock edge.

A useful event record might contain:

```cpp
struct Event {
    uint64_t time;
    Phase phase;
    EventType type;
    DeviceId source;
    uint64_t sequence;
};
```

Here, `Phase` represents a hardware-defined sub-time ordering, not a host operating-system thread. The `sequence` field can make otherwise equal events deterministic, but it must not substitute arbitrary software ordering for a known hardware rule.

For instance, if the hardware requires a memory request to be resolved before the next CPU data sample, the scheduler should express that dependency. It should not rely on the Blitter callback happening to be registered first.

##### Why not just use floating-point timestamps?

Floating-point time can be useful, but repeated conversions between CPU cycles, MFP cycles, and sound-clock ticks can introduce rounding errors. Hatari's clock and event code uses scaled integer time units; NeoST retains fractional timer phase rather than rounding away the fraction on every reload. 

For an emulator, an integer timebase or rational clock representation is usually easier to make deterministic.

#### 6. Shared clock does not necessarily mean shared phase

This is particularly relevant to the STE.

Hatari's machine-clock table lists both the CPU/bus clock and the sound-DMA clock at 8 MHz, but assigns them to different clock sources. The STE's sound clock comes from the separate oscillator, while the CPU clock is derived through the GSTMCU clock arrangement. 

Two clocks can have the same nominal frequency while having different phases. If an emulator represents both only as “one tick every eight master-clock periods,” it may inadvertently imply that their edges always align.

Whether this matters depends on the interaction being modeled. For a long DMA audio buffer, the distinction may be irrelevant to the resulting samples. For a specific simultaneous bus request, it may matter.

I would represent the clock domains explicitly:

- Master-clock domain.
- CPU/bus domain.
- Video/Shifter domain.
- MFP timer domain.
- Sound-DMA domain.
- Peripheral-specific domains, such as the IKBD's local clock.

Each domain should have a rational frequency ratio and, where relevant, a phase offset. This does **not** mean each domain must have its own thread or be advanced on every clock edge. It means the emulator can calculate when the next relevant edge occurs.

#### 7. My recommended architecture for your STE emulator

I would use a **discrete-event, common-timeline architecture with explicit clock domains and a transaction-level memory scheduler**.

That is a hybrid of the ideas visible in Hatari and NeoST, with Steem SSE's attention to the corner cases where simplified timing fails.

<box border radius="xl" padding={3} gap={2}>
  <box background="surface-secondary" radius="md" padding={3} align="center">
    **Common integer timeline**

    <text size="sm" textAlign="center">One monotonic emulated timebase; no host wall-clock dependence.</text>
  </box>
  <box align="center">
    <icon name="arrow-down-up" size="lg" />
  </box>
  <grid columns={2} gap={2}>
    <grid-item>
      <box border radius="md" padding={3} align="center">
        **Clock domains**

        <text size="sm" textAlign="center">Frequency ratios, phase, edge conversion</text>
      </box>
    </grid-item>
    <grid-item>
      <box border radius="md" padding={3} align="center">
        **Event scheduler**

        <text size="sm" textAlign="center">Dated transitions, deterministic ordering</text>
      </box>
    </grid-item>
  </grid>
  <box align="center">
    <icon name="arrow-down-up" size="lg" />
  </box>
  <box border radius="md" padding={3} align="center">
    **Shared bus and RAM scheduler**

    <text size="sm" textAlign="center">Arbitrates CPU, video, Blitter, disk DMA, and sound fetches</text>
  </box>
  <grid columns={3} gap={2}>
    <grid-item>
      <box background="surface" border radius="md" padding={2} align="center">
        <text size="sm" textAlign="center">68000</text>
      </box>
    </grid-item>
    <grid-item>
      <box background="surface" border radius="md" padding={2} align="center">
        <text size="sm" textAlign="center">GSTMCU / Shifter</text>
      </box>
    </grid-item>
    <grid-item>
      <box background="surface" border radius="md" padding={2} align="center">
        <text size="sm" textAlign="center">MFP / DMA / Blitter</text>
      </box>
    </grid-item>
  </grid>
</box>

##### The rules I would enforce

1. **Every device has explicit state.** A device's progress is not inferred from how often its update function happens to be called.

2. **All timed events use one common timeline.** Device clocks convert into that timeline using integer or rational arithmetic.

3. **Devices can be advanced lazily.** A PSG, timer, or DMA engine can calculate its state at a later time without running every intermediate clock, provided no relevant interaction was missed.

4. **Interactions force synchronization.** When a CPU register access reads a timer, when a device samples a signal, or when a bus request competes for RAM, advance the relevant state to the interaction's exact emulated time.

5. **RAM service is separate from device logic.** The Blitter decides what access it wants; the bus scheduler decides when it can happen.

6. **Same-time event ordering is explicit.** Use hardware-derived phase or priority rules, not incidental function-call order.

7. **Host time is separate.** Rendering and audio delivery follow real time, but never drive the internal hardware timeline.

#### 8. How far should you go toward half-cycle simulation?

I would not start by executing all 32 MHz master-clock edges. That would be a large implementation cost, and most device behavior can be represented by events or batched updates.

Instead, add finer timing only where a demonstrated hardware behavior needs it:

- Start with cycle-exact CPU bus accesses and a shared timeline.
- Add scheduled HBL/VBL, MFP, FDC, and Blitter events.
- Make memory requests explicit and model arbitration.
- Add sub-cycle or phase-aware ordering where a test demonstrates that cycle-level timing is insufficient.
- Only then consider modeling selected signal transitions at a finer granularity.

This approach is consistent with the strengths of the existing emulators: Hatari demonstrates the value of a central event system and scaled clock domains; NeoST demonstrates the value of explicit device models and a documented continuous timeline; Steem SSE demonstrates how real compatibility failures expose the need for more precise timing at specific boundaries. 

**The key recommendation:** design for accurate synchronization, not maximum clock-step frequency. You want to be able to reproduce the ordering and phase of every *relevant* interaction, while allowing each component to advance in large steps when nothing can observe the intermediate state. That is the best balance between hardware fidelity, code clarity, and emulator performance for your PAL STE.

---

## Turn 34 — 2026-10-28 22:59 UTC

### Question

> ok, lets go back to the 68000 study ; what are all the supported instructions families and variants, what is the instruction state machine., what are the main bus cycles, does the processor use microcode for its instructions or is there a documented decomposition of its instructions in more elementary operations on the chip cycle by cycle

### Answer

### Motorola 68000: instruction set, execution state machine, and bus cycles

For your Atari STE emulator, the key is to understand the 68000 at **three distinct levels**:

1. **Instruction-set level:** what each instruction does architecturally—register changes, memory accesses, condition codes, exceptions.
2. **Execution level:** how the processor sequences instruction fetch, effective-address calculation, operand processing, and completion.
3. **Bus and timing level:** when the 68000 requests memory, transfers words, acknowledges interrupts, or yields the bus.

These levels are related, but they are not interchangeable. Knowing that `MOVE.W (A0),D0` reads a word from memory does not, by itself, tell you exactly when the read happens relative to internal execution or how many cycles a particular effective address costs.

One important distinction up front: **the original MC68000 is not a processor for which Motorola published a complete, programmer-usable, per-internal-clock micro-operation trace for every instruction.** Its architecture, instruction encodings, bus protocol, and documented timing are well specified. The precise internal implementation is a different matter.

#### 1. Which instruction families does the original MC68000 support?

The authoritative starting point is Motorola's *M68000 Family Programmer's Reference Manual* and *M68000 8-/16-/32-Bit Microprocessors User's Manual*. The latter documents the bus protocol and timing tables; the former summarizes the instruction set and addressing modes. 

The Atari STE's 68000 implements the **original MC68000 instruction set**, not the full instruction set of later 680x0 processors. In particular, do not accidentally implement MC68010, 68020, or later instructions just because a general-purpose 68k reference lists them.

##### A. Instruction families and their principal variants

| Family | Instructions and important variants |
|---|---|
| Data movement | `MOVE.B/.W/.L`, `MOVEA.W/.L`, `MOVEQ`, `MOVEM.W/.L`, `MOVEP.W/.L`, `EXG`, `SWAP`, `EXT.W/.L`, `LEA`, `PEA` |
| Integer arithmetic | `ADD`, `ADDA`, `ADDI`, `ADDQ`, `ADDX`; `SUB`, `SUBA`, `SUBI`, `SUBQ`, `SUBX`; `MULS`, `MULU`, `DIVS`, `DIVU` |
| Compare and test | `CMP`, `CMPA`, `CMPI`, `CMPM`, `TST`, `CHK` |
| Logical | `AND`, `ANDI`, `OR`, `ORI`, `EOR`, `EORI`, plus immediate forms targeting the condition-code register or status register |
| Negation and clearing | `NEG`, `NEGX`, `NOT`, `CLR` |
| Bit manipulation | `BTST`, `BCHG`, `BCLR`, `BSET`; bit number can be immediate or taken from a data register |
| Shifts and rotates | `ASL`, `ASR`, `LSL`, `LSR`, `ROL`, `ROR`, `ROXL`, `ROXR`; register-count and immediate-count forms, plus memory forms where supported |
| Binary-coded decimal | `ABCD`, `SBCD`, `NBCD` |
| Branches and conditions | `BRA`, `BSR`, `Bcc` (all condition-code conditions), `DBcc`, `Scc` |
| Jumps and subroutines | `JMP`, `JSR`, `RTS`, `RTR`, `RTE` |
| Stack and procedure support | `LINK`, `UNLK`, `PEA`, `MOVEM`, `BSR`, `JSR`, `RTS` |
| Status and system control | `MOVE` to/from `SR`, `CCR`, and `USP` where permitted; `ORI/ANDI/EORI` to `CCR` or `SR`; `RESET`, `NOP`, `STOP`, `TRAP`, `TRAPV`, `ILLEGAL` |
| Multiprocessor / bus control | `TAS`, `TAS` on an eligible data-alterable operand; `TAS` performs a read-modify-write operation; `TRAP` and exception processing also have system-level effects |
| Special and exceptional behavior | `RESET`, `STOP`, `RTE`, `ILLEGAL`, `TRAP`, `TRAPV`, `CHK`, address-error and bus-error exception entry, interrupt acknowledge |

A few important clarifications:

- **Suffixes matter.** `.B`, `.W`, and `.L` specify byte, word, and longword operations where the instruction supports them. Some instructions do not support all sizes.
- **Address registers have special rules.** For example, `MOVEA` does not update the condition codes, and `ADDA`/`SUBA` operate on address registers rather than behaving exactly like ordinary data-register arithmetic.
- **Conditions are variants, not separate hardware instructions.** `BNE`, `BEQ`, `BHI`, `BLE`, and the other conditional branches share a condition-evaluation scheme, but differ in the condition they test.
- **The effective address changes the work required.** `ADD.W D0,D1`, `ADD.W (A0),D1`, and `ADD.W D0,(A0)` are not timing-equivalent, even though they belong to the same arithmetic family.

This is a family-level overview rather than an encoding table. For a real emulator, the full opcode map must also include reserved encodings, privilege restrictions, legal effective-address combinations, and instruction-specific size restrictions.

##### B. Which later 68k instructions must not be mistaken for MC68000 instructions?

Examples of instructions or features that belong to later processors include:

- `BKPT`, `MOVEC`, and `RTD` — associated with the MC68010 or later.
- `LINK.L`, bit-field instructions, `CAS`, `CHK2`, `CMP2`, `PACK`, and `UNPK` — later-family features, with availability varying by instruction.
- `MOVES`, `EXTB.L`, scaled indexing, and the 68020's richer effective-address forms — not original MC68000 functionality.

For the STE, the CPU's own instruction decoder should enforce the **original 68000 legal-instruction and addressing-mode rules**. Do not use a 68020 instruction decoder and simply assume that programs won't execute its extra instructions.

---

#### 2. The addressing modes are part of the instruction's execution

The 68000's instruction set is compact partly because many instructions can use the same set of addressing modes. Motorola documents 14 addressing modes for the original processor family. 

| Mode | Typical syntax | What the processor must do |
|---|---|---|
| Data register direct | `D0` | Read or update an internal register |
| Address register direct | `A0` | Read or update an address register, if allowed |
| Address indirect | `(A0)` | Read or write memory at the address in `A0` |
| Postincrement | `(A0)+` | Access memory, then increment the address register |
| Predecrement | `-(A0)` | Decrement the address register, then access memory |
| Displacement | `16(A0)` | Fetch an extension word, add signed displacement, access memory |
| Indexed | `8(A0,D1.W)` | Fetch displacement, combine base and index, access memory |
| Absolute short | `($1234).W` | Fetch an extension word and form an address |
| Absolute long | `($00123456).L` | Fetch two extension words and form an address |
| PC-relative displacement | `label(PC)` | Use the instruction's PC-relative base and an extension word |
| PC-relative indexed | `label(PC,D0.W)` | Combine PC-relative base, index, and displacement |
| Immediate | `#$1234` | Fetch operand data from the instruction stream |
| Implied / register-specific | `SR`, `CCR`, `USP`, etc. | Operate on a register or state defined by the instruction |

These are not merely assembler conveniences. They change the number and order of instruction-stream fetches, internal calculations, and data bus transactions.

For instance, a longword read from memory on the original 68000 normally takes **two 16-bit bus transfers**. A byte access uses the appropriate byte lane. An odd-address word or longword access causes an address-error exception rather than being transparently split into arbitrary byte operations.

That last point is essential for Atari compatibility: the CPU does not make every unaligned access work just because the emulator's host computer permits it.

---

#### 3. What is the 68000's instruction state machine?

There is no single small, publicly documented state machine of the form “state 0 means fetch, state 1 means decode, state 2 means add” that fully describes every MC68000 instruction.

Instead, think of execution as **an instruction-sequencing process that uses internal operations and invokes bus cycles as required**. Different instructions follow different paths, and exceptions or bus delays can alter the path.

<box border radius="lg" padding={3} gap={2}>
  <box background="surface-secondary" radius="md" padding={3} align="center">
    **1. Instruction prefetch / fetch**

    <text size="sm" textAlign="center">Obtain instruction words from memory; maintain the instruction stream and PC.</text>
  </box>
  <box align="center"><icon name="arrow-down" size="lg" /></box>
  <box background="surface-secondary" radius="md" padding={3} align="center">
    **2. Decode**

    <text size="sm" textAlign="center">Interpret opcode fields, operand size, addressing modes, and required extensions.</text>
  </box>
  <box align="center"><icon name="arrow-down" size="lg" /></box>
  <box background="surface-secondary" radius="md" padding={3} align="center">
    **3. Resolve operands**

    <text size="sm" textAlign="center">Calculate effective addresses; fetch extension words or immediate data; read source operands as needed.</text>
  </box>
  <box align="center"><icon name="arrow-down" size="lg" /></box>
  <box background="surface-secondary" radius="md" padding={3} align="center">
    **4. Execute / update state**

    <text size="sm" textAlign="center">Perform arithmetic, logical, shift, comparison, or control-flow operations; update registers and flags when specified.</text>
  </box>
  <box align="center"><icon name="arrow-down" size="lg" /></box>
  <box background="surface-secondary" radius="md" padding={3} align="center">
    **5. Complete required writes and control changes**

    <text size="sm" textAlign="center">Write results to memory or registers; resolve branches, stack changes, and instruction-specific completion.</text>
  </box>
  <text size="sm" color="secondary">Conceptual sequence, not a literal fixed five-state silicon machine. Prefetch can overlap with execution, and an instruction may interleave internal work with bus transfers.</text>
</box>

The sequence is not always linear. An instruction may need several extension-word fetches, a read followed by a write, repeated register or memory operations, or exception processing. Prefetch also means that the word the CPU fetches from memory is not necessarily the word it immediately executes next.

##### The instruction prefetch queue matters

The original 68000 uses a small instruction-prefetch mechanism. In practical terms:

- The CPU can fetch instruction words before it actually needs to execute them.
- A branch, jump, exception, or other control-flow change may invalidate words already fetched from the old instruction stream.
- Memory-mapped I/O can make the timing and ordering of instruction fetches observable.
- A write to memory that also contains upcoming instructions can interact with already-prefetched words.

For an emulator, a single `PC += instruction_length` followed by immediate execution can reproduce many programs, but it does not inherently reproduce these effects. A higher-accuracy CPU core needs an explicit model of instruction fetches and the relevant prefetch state.

##### Exception processing is a separate execution path

Interrupts and exceptions are not ordinary instructions, even though their entry sequences consume processor time and perform memory accesses.

The processor may need to:

1. Recognize an exception condition or interrupt request.
2. Establish the correct supervisor state and stack.
3. Save the required return state.
4. Fetch the exception vector.
5. Transfer control to the handler.

The exact sequence and saved state depend on the exception. Address errors and bus errors are especially important because the CPU may need to preserve information about the failed access. The original 68000's exception frame is not interchangeable with the later MC68010's format.

For your emulator, model exception entry as a defined CPU operation with real bus accesses, not just as `PC = vector_table[vector]`.

#### 4. The main bus cycles

The original MC68000 has a **16-bit external data bus**, a 24-bit address bus, and separate upper- and lower-byte strobes. Its standard bus protocol is described in Motorola's manual as a sequence of eight bus states, `S0` through `S7`, for a basic bus cycle. 

At 8 MHz, one CPU clock period is 125 ns. A four-clock bus cycle therefore takes 500 ns **before any wait states**. This is the nominal bus-cycle duration, not the duration of every instruction.

##### A. Ordinary read cycle

<box border radius="lg" padding={3} gap={2}>
  <row align="center" gap={2}>
    <box background="surface-secondary" radius="md" padding={2} flex="1" align="center">
      **S0–S1**

      <text size="xs" textAlign="center">Select access</text>
    </box>
    <icon name="arrow-right" size="sm" color="secondary" />
    <box background="surface-secondary" radius="md" padding={2} flex="1" align="center">
      **S2–S3**

      <text size="xs" textAlign="center">Assert strobes</text>
    </box>
    <icon name="arrow-right" size="sm" color="secondary" />
    <box background="surface-secondary" radius="md" padding={2} flex="1" align="center">
      **S4–S5**

      <text size="xs" textAlign="center">Wait / sample</text>
    </box>
    <icon name="arrow-right" size="sm" color="secondary" />
    <box background="surface-secondary" radius="md" padding={2} flex="1" align="center">
      **S6–S7**

      <text size="xs" textAlign="center">Finish cycle</text>
    </box>
  </row>
  <text color="secondary" size="sm">Conceptual grouping of the eight bus states; individual signal edges are defined by the manual's timing diagrams.</text>
</box>

A typical read works like this:

1. The CPU establishes the function code, identifying the kind of access, and indicates a read.
2. It puts the address on the address bus.
3. It asserts `AS` (address strobe) and the appropriate data strobe: `UDS`, `LDS`, or both.
4. The external device supplies data.
5. The CPU waits for an accepted cycle-termination response—normally `DTACK`, or a bus error through `BERR`.
6. The CPU completes the transfer and releases the bus signals.

If the target is not ready, the CPU inserts **wait states**. Thus, two identical instructions can take different amounts of elapsed time if their memory accesses experience different wait states.

##### B. Ordinary write cycle

The basic write sequence is similar, but the CPU drives the data bus rather than sampling data from it.

The address and write direction are established, the data becomes valid, the appropriate strobes are asserted, and the processor waits for the target to terminate the cycle. A memory-mapped device can therefore observe the timing of writes to its registers.

This is one reason an emulator should not implement every instruction as an instantaneous memory operation followed by a fixed delay. A write may trigger a video, sound, or peripheral state change at a specific point in the shared machine timeline.

##### C. Longword access

A longword is 32 bits wide, but the original 68000's external data bus is only 16 bits wide.

For example:

```asm
MOVE.L  (A0),D0
```

If `A0` points to an aligned address, the memory operand normally requires two word reads:

- Read the word at `A0`.
- Read the word at `A0 + 2`.
- Assemble the two words into the 32-bit result.

A longword write similarly requires two word transfers. Instruction-extension fetches also consume bus bandwidth; they are not free just because the emulator can decode them from an array.

An odd-address word or longword access is different: the original 68000 raises an address error instead of transparently splitting it into byte transfers.

##### D. Read-modify-write cycle

Some instructions need to read a memory operand, change it, and write it back. The bus protocol supports a read-modify-write sequence in which `AS` remains asserted across the operation, making the sequence distinguishable from two unrelated accesses. 

Examples include eligible memory-destination arithmetic and logical instructions, bit-manipulation operations, and `TAS`.

For the emulator, keep the following concepts separate:

- **Instruction semantics:** read operand, calculate result, update flags as specified.
- **Bus sequence:** perform the read and write with the correct ordering and signals.
- **Atomicity / arbitration:** model whether another bus master can intervene in that sequence.

Do not implement a memory-destination operation as a generic read followed by a generic write if that loses the hardware's read-modify-write signaling or arbitration behavior.

##### E. CPU-space and interrupt-acknowledge cycles

The 68000 also performs special bus cycles, notably CPU-space cycles used for operations such as interrupt acknowledgement. Interrupt handling is not simply a normal memory read of a vector address: the processor first acknowledges the interrupt through its bus protocol, then performs the required exception-entry operations and vector fetches.

For an Atari STE, this matters because interrupt requests can originate from the MFP and other hardware, and the CPU's recognition of those requests is affected by the interrupt mask, instruction timing, and exception processing.

##### F. Bus arbitration is a different mechanism

The 68000 supports bus arbitration through signals such as `BR`, `BG`, and `BGACK`. These govern a request to relinquish the bus and the transfer of bus ownership. They are distinct from `DTACK`, which terminates an individual bus transaction.

For the STE emulator, this distinction is crucial:

- **Bus-cycle state** answers: what transaction is the CPU currently performing?
- **Bus arbitration state** answers: who is permitted to use the bus?
- **Wait-state state** answers: has the current transaction completed yet?

The Atari's video and DMA hardware make memory-access timing observable, so these should not be collapsed into one generic “CPU is waiting” flag.

---

#### 5. Does the 68000 use microcode?

The careful answer is: **the public MC68000 documentation does not provide a complete, authoritative internal microcode listing or a full per-clock micro-operation schedule for every instruction.**

It documents the instruction set, bus protocol, exception behavior, and instruction timing, including the number of reads and writes for many instructions. It does not give emulator developers a complete internal control-store program that can simply be translated into code.

There are three things worth distinguishing.

| Concept | What it means for the 68000 |
|---|---|
| Architectural instruction definition | Documented: operands, results, flags, exceptions, legal forms |
| External bus-cycle sequence and timing | Documented to a useful hardware-engineering level |
| Exact internal micro-operation sequence, cycle by cycle | Not fully documented in the public programmer's/user's manuals |

The internal implementation necessarily has control logic that sequences the datapath and bus interface. It is not safe, however, to infer the exact internal design—such as a particular microcode format, control-store layout, or number of microinstructions—from the programmer-visible documentation alone.

In other words, a software emulator's sequence of “read source, calculate, write destination” is a **model of the processor's behavior**, not proof that the physical chip executes those exact software-like steps internally.

##### What about reverse engineering?

There are several ways to go deeper than the manuals:

- **Instruction timing tables** provide baseline cycle counts and, in many cases, read/write counts. They constrain the possible implementation.
- **Bus traces** reveal external addresses, transfer ordering, strobes, wait states, and timing.
- **Hardware measurements and reverse engineering** can reveal additional behavior, especially around prefetch, interrupts, and unusual instruction cases.
- **Existing CPU cores** encode tested models of instruction behavior and timing. They are valuable references, but different cores may model internal timing at different levels of precision.

None of these should automatically be mistaken for an official, complete silicon microcode specification.

---

#### 6. How to represent this in your emulator

For your STE emulator, I would separate the 68000 core into three cooperating parts rather than build one enormous function per instruction.

<box border radius="lg" padding={3} gap={2} align="center">
  <box background="surface" border radius="md" padding={3} width="100%" align="center">
    **Instruction decoder and semantics**

    <text size="sm" textAlign="center">Opcode, size, legal addressing modes, register and flag effects</text>
  </box>
  <icon name="arrow-down-up" size="lg" color="secondary" />
  <box background="surface" border radius="md" padding={3} width="100%" align="center">
    **Execution sequencer**

    <text size="sm" textAlign="center">Prefetch, extension words, effective addresses, operand operations, exceptions, instruction completion</text>
  </box>
  <icon name="arrow-down-up" size="lg" color="secondary" />
  <box background="surface" border radius="md" padding={3} width="100%" align="center">
    **Bus interface and shared timeline**

    <text size="sm" textAlign="center">Timestamped reads and writes, byte lanes, wait states, bus ownership, interrupt sampling</text>
  </box>
</box>

The bottom layer should communicate with the Atari's memory map and bus scheduler, not directly access a flat RAM array for every operation. That lets the CPU's accesses interact with the Shifter, GSTMCU, Blitter, and DMA devices.

##### Two useful implementation levels

**Level 1 — instruction-level execution with bus-aware timing**

- Decode and execute one instruction at a time.
- Preserve all architectural effects and exceptions.
- Calculate timing from documented instruction and effective-address rules.
- Timestamp memory accesses and explicitly model important bus-visible effects.
- Account for prefetch and interrupt behavior to the degree required by your accuracy target.

This is a good starting point, but a fixed cycle count added after an instruction cannot represent every interaction accurately.

**Level 2 — bus-cycle-aware or micro-operation execution**

- Represent instruction execution as smaller operations.
- Each bus operation has an address, direction, transfer size, and completion time.
- Internal operations can consume time between bus transfers.
- The CPU can be interrupted or delayed at the points where the hardware allows it.
- Prefetch, exceptions, read-modify-write sequences, and arbitration become explicit state.

This level is much more suitable when you need accurate CPU/video/DMA interactions. It still does not require simulating every master-clock edge: your shared timeline and scheduler can advance directly to the next meaningful operation or event.

##### A concrete example: `ADD.W (A0),D0`

At the architectural level, the instruction reads a word from memory, adds it to the low word of `D0`, writes the result to `D0`, and updates the relevant condition codes.

At the emulator level, you need to account for:

1. Instruction-word availability and decoding.
2. A memory read at the address held in `A0`.
3. The internal addition and flag calculations.
4. Any instruction-stream prefetch that overlaps with execution.
5. Completion and the timing at which the next instruction can proceed.

The key insight is that **the arithmetic is the easy part; reproducing the ordering of instruction fetches, bus accesses, and internal execution is the hard part**.

##### Recommended reference documents

<box gap={3}>
  <row align="start" gap={3}>
    <AsyncImage query="Motorola M68000 8 16 32 bit microprocessors user's manual cover" aspectRatio="3:4" maxWidth="88px" />
    <box flex="1" gap={1}>
      **M68000 8-/16-/32-Bit Microprocessors User's Manual**

      <text color="secondary" size="sm">Primary reference for bus operations, signals, exception processing, and instruction execution timing.</text>

      <text><Link url="https://www.nxp.com/docs/en/reference-manual/MC68000UM.pdf" title="Read the user's manual (PDF)" /></text>
    </box>
  </row>
  <divider color="subtle" />
  <row align="start" gap={3}>
    <AsyncImage query="Motorola M68000 Family Programmer's Reference Manual cover" aspectRatio="3:4" maxWidth="88px" />
    <box flex="1" gap={1}>
      **M68000 Family Programmer's Reference Manual**

      <text color="secondary" size="sm">Instruction definitions, opcode families, condition codes, addressing modes, and legal operand combinations. Check the processor-specific tables because this manual also covers later family members.</text>

      <text><Link url="https://www.nxp.com/docs/en/reference-manual/M68000PRM.pdf" title="Read the programmer's reference manual (PDF)" /></text>
    </box>
  </row>
</box>

My recommendation is to study the 68000 in this order: **opcode encoding and legality → effective-address execution → bus cycles → prefetch and exceptions → interrupt timing → instruction-level timing validation**. That progression gives you a reliable foundation before tackling the hardest Atari-specific timing interactions.

---

If you want, I can:

- Explain the 68000 execution sequencer in detail
- Describe how to model 68000 bus interface and shared timeline
- Give a detailed stepwise example of ADD.W (A0),D0 execution

---
