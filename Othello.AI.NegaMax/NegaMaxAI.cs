using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Othello.Contract;

namespace Othello.AI.NegaMax;

public class NegaMaxAI : IOthelloAI
{
    public string Name => "NegaMax AI";

    // The game does have a 5 second time limit, so we need to make sure we don't search too deep.
    // This is a brute force algorithm, so it will search as deep as it can within the time limit.
    // The deeper we search, the better the AI will play, but the longer it will take to find a move.
    // A depth of 10 is pretty good for Othello, and with Iterative Deepening, we can get a pretty good move in 5 seconds.

    private const int MaxDepth = 10;

    public async Task<Move?> GetMoveAsync(BoardState board, DiscColor yourColor, CancellationToken ct)
    {
        // Wrap the CPU-heavy search in Task.Run so it runs on a background thread
        // and doesn't block the Avalonia UI thread.
        return await Task.Run(() => 
        {
            var validMoves = GetValidMoves(board, yourColor);
            if (validMoves.Count == 0) return null;

            Move? bestMoveOverall = validMoves[0];

            try
            {
                // Iterative deepening: Search progressively deeper until time runs out
                for (int currentDepth = 1; currentDepth <= MaxDepth; currentDepth++)
                {
                    Move bestMoveForDepth = validMoves[0];
                    int bestScoreForDepth = int.MinValue;

                    foreach (var move in validMoves)
                    {
                        ct.ThrowIfCancellationRequested();

                        var newBoard = board.Clone();
                        ApplyMove(newBoard, move, yourColor);

                        // The score for this move is the MINUS of the score for the opponent's best response
                        // We pass in -Beta for Alpha and -Alpha for Beta
                        int score = -NegaMax(newBoard, currentDepth - 1, int.MinValue + 1, int.MaxValue, GetOpponentColor(yourColor), ct);

                        if (score > bestScoreForDepth)
                        {
                            bestScoreForDepth = score;
                            bestMoveForDepth = move;
                        }
                    }

                    // If we completely finished searching this depth without timing out, save the best move
                    bestMoveOverall = bestMoveForDepth;
                }
            }
            catch (OperationCanceledException)
            {
                // The 5 second time limit was reached, we are cut off before this anyways.
                // We swallow the exception and just return the best move found from the last fully completed depth.
            }

            return bestMoveOverall;
        }, ct);
    }

    private int NegaMax(BoardState board, int depth, int alpha, int beta, DiscColor color, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var validMoves = GetValidMoves(board, color);

        // Terminal node or depth limit reached
        if (depth == 0 || validMoves.Count == 0)
        {
            // If we have no moves, the game might be over, or we just pass.
            // If opponent also has no moves, game over.
            if (validMoves.Count == 0)
            {
                 var opponentMoves = GetValidMoves(board, GetOpponentColor(color));
                 if (opponentMoves.Count == 0)
                 {
                     // Game over
                     return EvaluateTerminalState(board, color);
                 }
                 
                 // Pass turn case
                 // If we pass, our score is the negative of the opponent's score after they play
                 return -NegaMax(board, depth, -beta, -alpha, GetOpponentColor(color), ct);
            }

            return EvaluateBoard(board, color);
        }

        int maxScore = int.MinValue + 1;

        foreach (var move in validMoves)
        {
            var newBoard = board.Clone();
            ApplyMove(newBoard, move, color);

            int score = -NegaMax(newBoard, depth - 1, -beta, -alpha, GetOpponentColor(color), ct);

            if (score > maxScore)
            {
                maxScore = score;
            }

            if (maxScore > alpha)
            {
                 alpha = maxScore;
            }

            if (alpha >= beta)
            {
                break; // Alpha-Beta cutoff
            }
        }

        return maxScore;
    }

    private int EvaluateTerminalState(BoardState board, DiscColor color)
    {
         int myCount = 0;
         int oppCount = 0;
         for (int r=0; r<8; r++)
         {
             for (int c=0; c<8; c++)
             {
                 if (board.Grid[r,c] == color) myCount++;
                 else if (board.Grid[r,c] == GetOpponentColor(color)) oppCount++;
             }
         }

         if (myCount > oppCount) return 10000 + myCount; // Win, return 10000 on top of the myCount so the AI will aggressively chase the win.
         if (myCount < oppCount) return -10000 - oppCount; // Loss, return -10000 on top of the oppCount in a similar mindset.
         return 0; // Draw
    }

    private int EvaluateBoard(BoardState board, DiscColor color)
    {
        // Simple heuristic: Count discs + weight corners heavily
        // Counting discs is not a good strategy in Othello, the person who gets the most discs early almost always loses.
        // So we give spaces weight for the AI to prioritize the right method. Plus actively watching a comeback is always fun.
        // Hence, the heutristic below.

        int score = 0;
        DiscColor opponent = GetOpponentColor(color);

        // Positional weights
        // Corners are worth 100, edges are worth 10, and the rest are worth 1.
        // Corners are pretty crucial in Othello, so we want to make sure we get them.
        int[,] weights = {
            { 100, -20,  10,   5,   5,  10, -20, 100 },
            { -20, -50,  -2,  -2,  -2,  -2, -50, -20 },
            {  10,  -2,  -1,  -1,  -1,  -1,  -2,  10 },
            {   5,  -2,  -1,  -1,  -1,  -1,  -2,   5 },
            {   5,  -2,  -1,  -1,  -1,  -1,  -2,   5 },
            {  10,  -2,  -1,  -1,  -1,  -1,  -2,  10 },
            { -20, -50,  -2,  -2,  -2,  -2, -50, -20 },
            { 100, -20,  10,   5,   5,  10, -20, 100 }
        };

        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                if (board.Grid[r, c] == color)
                {
                    score += weights[r, c];
                }
                else if (board.Grid[r, c] == opponent)
                {
                    score -= weights[r, c];
                }
            }
        }

        // Add mobility to score (number of valid moves we have vs opponent)
        // This allows the AI to know how "good of a position" it is in.
        // If the AI has more choices than the opponent, it is in a better position.
        int myMoves = GetValidMoves(board, color).Count;
        int oppMoves = GetValidMoves(board, opponent).Count;
        
        score += (myMoves - oppMoves) * 5;

        return score;
    }

    private DiscColor GetOpponentColor(DiscColor color)
    {
        return color == DiscColor.Black ? DiscColor.White : DiscColor.Black;
    }

    private List<Move> GetValidMoves(BoardState board, DiscColor color)
    {
        var moves = new List<Move>();
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                if (IsValidMove(board, new Move(r, c), color))
                {
                    moves.Add(new Move(r, c));
                }
            }
        }
        return moves;
    }

    private bool IsValidMove(BoardState board, Move move, DiscColor color)
    {
        // This acts as an observer of the game. It checks if a move is valid.
        // First, check if the spot is empty.
        if (board.Grid[move.Row, move.Column] != DiscColor.None) return false;

        // Check all 8 directions, the compass of the game.
        // dr is Delta Row, dc is Delta Column. 
        // For example, (-1, -1) is up-left. (-1, 0) is up. (0, 1) is right. etc.
        int[] dr = { -1, -1, -1, 0, 0, 1, 1, 1 };
        int[] dc = { -1, 0, 1, -1, 1, -1, 0, 1 };
        DiscColor opponent = GetOpponentColor(color);

        for (int i = 0; i < 8; i++)
        {
            int r = move.Row + dr[i];
            int c = move.Column + dc[i];
            int count = 0;

            // This makes sure that we stop on an empty space, the edge of the board, or our own color.
            while (r >= 0 && r < 8 && c >= 0 && c < 8 && board.Grid[r, c] == opponent)
            {
                r += dr[i];
                c += dc[i];
                count++;
            }
            // If we hit our own color, we are on the board and we have a count > 0, then the move is valid.
            // YES to all three allows return true.
            if (r >= 0 && r < 8 && c >= 0 && c < 8 && board.Grid[r, c] == color && count > 0)
            {
                return true;
            }
        }
        return false; // If we get here, the move is invalid.
    }

    private void ApplyMove(BoardState board, Move move, DiscColor color)
    {
        // Apply the move to the board.
        // Same compass directions as before. Logic is similar to IsValidMove. This is the actual executor of the move.
        int[] dr = { -1, -1, -1, 0, 0, 1, 1, 1 };
        int[] dc = { -1, 0, 1, -1, 1, -1, 0, 1 };
        DiscColor opponent = GetOpponentColor(color);

        board.Grid[move.Row, move.Column] = color;

        for (int i = 0; i < 8; i++)
        {
            var path = new List<Move>(); // Saves the path of opponent discs to be flipped.
            int r = move.Row + dr[i];
            int c = move.Column + dc[i];

            while (r >= 0 && r < 8 && c >= 0 && c < 8 && board.Grid[r, c] == opponent)
            {
                path.Add(new Move(r, c));
                r += dr[i];
                c += dc[i];
            }

            if (r >= 0 && r < 8 && c >= 0 && c < 8 && board.Grid[r, c] == color && path.Count > 0)
            {
                foreach (var m in path)
                {
                    board.Grid[m.Row, m.Column] = color;
                }
            }
        }
    }
}
