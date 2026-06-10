# Spec

AsciiDraw is a desktop app similar to Monodraw (https://monodraw.helftone.com/) that allows users to create text-based art like diagrams, layouts, flow charts.

## UI

AsciiDraw app has a toolbar docked to the top of the window, a status bar docked to the bottom of the window, a layout tree docked to the left of the window, a properties panel docked to the right of the window, and a scrollable canvas in the center of the window.

UI:

```
┌──────────────────────────────────────────────────────────────────────────────────────────────────┐
│unamed.asciidraw - AsciiDraw                                                                _ O X │
├──────────────────────────────────────────────────────────────────────────────────────────────────┤
│┌──────┬──────┬──────┐┌──────┬──────┬──────┐                                                      │
││ Open │ Save │Export││ Rect │ Text │ Line │                                                      │
│└──────┴──────┴──────┘└──────┴──────┴──────┘                                                      │
├──────────────────────┬─────────────────────────────────────────────────┬─────────────────────────┤
│> Rect                │                                                 │Properties               │
│> Rect                │                                                 │       ┌────────────────┐│
│> Group 1             │                                                 │Name:  │Rect1           ││
│    > Rect            │                                                 │       └────────────────┘│
│    > Line            │                                                 │       ┌────────────────┐│
│    > Rect            │                                                 │Style: │Normal          ││
│> Rect                │                                                 │       └────────────────┘│
│                      │                                                 │       ┌──┐              │
│                      │                                                 │       │  │Transparent   │
│                      │                                                 │       └──┘              │
│                      │                                                 │Text:                    │
│                      │                                                 │┌───────────────────────┐│
│                      │                                                 ││                       ││
│                      │                                                 ││         Hello         ││
│                      │                                                 ││                       ││
│                      │                                                 │└───────────────────────┘│
├──────────────────────┴─────────────────────────────────────────────────┴─────────────────────────┤
│Selection: Rect1: 20x30 at (8, 41)                                                                │
└──────────────────────────────────────────────────────────────────────────────────────────────────┘
```

## Features

- Open, save file with format ".asciidraw" (actually json format) that contains all the information of the drawing, including the position, size, style, and text content of each element.
- Export file with format ".txt" that contains the text-based art in plain text format.
- Export file with format ".svg" or ".png" that contains the text-based art in SVG or PNG format.
- Draw rectangles, lines, and text boxes on the canvas. Each element can be selected, moved, resized, and styled with different line styles (normal, dashed, dotted) and fill styles (transparent, solid).
- There are only two types of elements: rectangle and line. Text box is a type of rectangle element that has a border style "None" and a fill style "Transparent".
- A rectangle element has a position (x, y), a size (width, height), a line style (normal, dashed, dotted), and a fill style (transparent, solid).
- A line element has a start point (x1, y1) and an end point (x2, y2), a line style (normal, dashed, dotted) and start / end arrow style (none, triangle).
- A line element can link two rectangle elements. When a rectangle element is moved, the line elements linked to it will be updated accordingly.
- Group elements together and ungroup them. Grouped elements can be moved together.
- Undo and redo actions.
