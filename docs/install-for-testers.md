# OtterLogic — install and test drive

A pre-release build of a Rhino 8 plug-in. Two front-ends install together: a
Rhino command and a Grasshopper tab.

**You need:** Rhino 8 on Windows, any 8.x. Nothing else — Rhino already ships
the .NET runtime this uses.

**You get:** one file, `otterlogic-0.1.0-rh8_0-win.yak`.

---

## 1. Unblock the file

Windows silently marks files that arrive by email, Teams or download, and Rhino
will then refuse to load the plug-in with no error message. Do this first, even
if it seems unnecessary.

Right-click the `.yak` file → **Properties** → if there is an **Unblock**
checkbox at the bottom, tick it → **OK**.

No checkbox means it was never blocked. Move on.

## 2. Close Rhino

Fully closed, not minimised. The installer cannot replace files Rhino is
holding open.

## 3. Install it

Open **PowerShell** (Start menu → type `powershell` → Enter) and paste this,
with the real path to where you saved the file:

```
& 'C:\Program Files\Rhino 8\System\Yak.exe' install C:\Users\you\Downloads\otterlogic-0.1.0-rh8_0-win.yak
```

Tip: type `& 'C:\Program Files\Rhino 8\System\Yak.exe' install ` and then drag
the `.yak` file from Explorer into the PowerShell window — it pastes the path
for you.

It installs to your user profile. No admin rights, nothing touched outside
`%APPDATA%`.

### If you would rather not use PowerShell

The `.yak` is a renamed zip, so you can do it by hand in Explorer:

1. Copy the file, rename the copy to `otterlogic.zip`, and extract it.
2. In the address bar, go to
   `%APPDATA%\McNeel\Rhinoceros\packages\8.0`
3. Create a folder `OtterLogic`, and inside it a folder `0.1.0`.
4. Put everything you extracted into that `0.1.0` folder.
5. Back in `OtterLogic`, create a text file `manifest.txt` containing just
   `0.1.0` — no quotes, no line break.

The result should be
`...\packages\8.0\OtterLogic\0.1.0\OtterLogic.rhp` and its siblings.

## 4. Start Rhino

Everything loads itself — no Plug-in Manager step, no Grasshopper developer
settings step.

**Check it worked:** type `Otter` at the Rhino command line. If
`OtterTruss2D` appears in the autocomplete, you are in.

An **OtterLogic** toolbar also arrives. The first time it appears floating —
drag it into the tab strip next to Standard and Curve Tools and Rhino will
remember it there.

---

## Test drive: Truss 2D

**Set up.** Draw two curves, one above the other. Straight lines are fine;
curved chords work too.

### In Rhino

Run `OtterTruss2D` and follow the prompts. It asks for the top chords, then the
bottom chords, then walks you through the bracing pattern, whether to flip it,
whether to cap the ends, a division count, extra snap points and panel spacing.

It previews the truss live and lets you keep changing the type, flip, divisions,
spacing and end posts before anything is committed to the document.

**Both chord picks take a set, not a single curve.** Pick the top chords, press
**Enter**, then pick the bottom chords *in the same order*, press **Enter**.
Top chord 1 pairs with bottom chord 1, so pick order is truss order — that is
how you get a whole bay in one run.

The counts have to match. Three top chords and two bottom ones stops with a
message rather than guessing.

### In Grasshopper

**OtterLogic** tab → **Structural Form** panel → **Truss 2D**. Same inputs, as
ports.

**Truss Type** sits in the same panel — drop it on the canvas for a dropdown of
the bracing patterns and wire it into the Type input. The input's own
right-click menu carries the same list if you would rather not add a second
object.

---

## Feedback

This is a development build, so it carries debug symbols — which is useful: if
something goes wrong, the error will name the exact line.

If it breaks, the most helpful thing you can send is whatever appears in the
Rhino command line at the moment it fails, plus what you had selected and which
options you picked.

## Removing it

Close Rhino, then:

```
& 'C:\Program Files\Rhino 8\System\Yak.exe' uninstall OtterLogic
```

Or delete `%APPDATA%\McNeel\Rhinoceros\packages\8.0\OtterLogic` if you installed
by hand.
