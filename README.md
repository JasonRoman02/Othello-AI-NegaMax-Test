# NegaMax AI Implementation Details

This repository contains the `Othello.AI.NegaMax` project, an AI built to play Othello using the NegaMax algorithm with Alpha-Beta pruning, as requested for the assignment.

## 1. How the AI Works (NegaMax & Alpha-Beta Pruning)

The core logic of this AI is driven by the **NegaMax** algorithm. NegaMax is a streamlined implementation of Minimax. In a zero-sum game like Othello, the highest score for one player is exactly the lowest score for the other. By using the property `max(a, b) == -min(-a, -b)`, the AI evaluates the board from the _current_ player's perspective and negates the score as it returns up the recursive search tree. This avoids needing separate "Maximize" and "Minimize" loops.

To make the AI fast enough to search deeply without timing out, **Alpha-Beta Pruning** is used.

- **Alpha** represents the minimum score the AI is guaranteed to get.
- **Beta** represents the maximum score the opponent will allow.
  If a branch of the tree evaluates to a score higher than `Beta`, the AI stops searching that branch (`break`), because the opponent would never voluntarily choose a path that gives us that much advantage. This saves millions of calculations.

## 2. Iterative Deepening

The Othello Engine enforces a strict 5-second computation limit. To maximize the AI's search depth without forfeiting the turn from a timeout, the AI uses **Iterative Deepening**.

Instead of searching to a hardcoded depth (e.g., depth 5) and risking a timeout if the board is complex, the AI runs in a fast loop:

1. Search all moves at Depth 1. Save the best move.
2. Search all moves at Depth 2. Save the best move.
3. Search all moves at Depth N. Save the best move.

The entire NegaMax loop is wrapped in a `try/catch` block listening for the `CancellationToken`'s `OperationCanceledException`. When the 5 seconds run out, the exception is caught, the incomplete deep search is abandoned, and the AI instantly returns the best move it found from the last _fully completed_ depth. No turns are ever forfeited.

## 3. The Custom Scoring Method

When the `NegaMax` function hits a depth limit (or when time runs out), it must score the board. Simply counting discs is a notoriously bad strategy in Othello early in the game. The `EvaluateBoard` function uses two main metrics instead:

1.  **Positional Weights**
    The 8x8 board is assigned static weights.
    - **Corners** are worth `+100` because once captured, they can never be flipped.
    - **C-Squares and X-Squares** (the squares immediately adjacent to the corners) are penalized heavily (`-20` and `-50`) because playing there often gives the opponent a chance to capture the corner on their next turn.
    - **Edges** and center spaces are given slightly positive or neutral weights.

2.  **Mobility**
    The number of available valid moves is calculated for both the AI and the opponent. The difference `(MyMoves - OpponentMoves)` is multiplied by a weight and added to the score. Restricting the opponent's choices while maximizing our own is a key strategy for winning.

## 4. Terminal State Evaluation

If the game naturally ends during the NegaMax depth search (neither player has any valid moves left), the AI switches to `EvaluateTerminalState`. Here, the heuristic is abandoned, and the actual disc count determines the score. A win returns heavily positive (`+10000 + discs`), a loss returns heavily negative (`-10000 - discs`), and a draw returns `0`. This ensures that if the AI sees a guaranteed forced win within its depth limit, it heavily prefers that path regardless of positional weights.
