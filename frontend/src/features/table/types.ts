export interface CardDto {
  rank: string;
  suit: string;
}

export interface SeatDto {
  index: number;
  playerId: string | null;
  playerName: string | null;
  stack: number;
  pendingRebuyChips: number;
  isAllIn: boolean;
  isFolded: boolean;
  currentBet: number;
  holeCards: CardDto[] | null;
  revealedHandName: string | null;
  isSittingOut: boolean;
}

export interface PotDto {
  amount: number;
  winnerPlayerIds: string[];
  eligiblePlayerIds: string[];
}

export interface LegalActionsDto {
  canCheck: boolean;
  canCall: boolean;
  callAmount: number;
  minRaiseTo: number;
  maxRaiseTo: number;
}

export interface HandDto {
  street: "Preflop" | "Flop" | "Turn" | "River" | "Showdown";
  board: CardDto[];
  currentActorPlayerId: string | null;
  actionDeadlineUtc: string | null;
  totalPot: number;
  result: PotDto[] | null;
  currentLegalActions: LegalActionsDto | null;
}

export interface EquityDto {
  playerId: string;
  winPercent: number;
  tiePercent: number;
}

export interface TableStateDto {
  tableId: string;
  name: string;
  status: "WaitingForPlayers" | "Playing";
  minBuyIn: number;
  maxBuyIn: number;
  seats: SeatDto[];
  spectators: string[];
  waitlistCount: number;
  hand: HandDto | null;
  equity: EquityDto[] | null;
  nextHandStartUtc: string | null;
}

export interface ChatMessageDto {
  userId: string;
  message: string;
  isSpectator: boolean;
  sentAtUtc: string;
}

export const BettingActionType = {
  Fold: 0,
  Check: 1,
  Call: 2,
  Bet: 3,
  Raise: 4,
} as const;
