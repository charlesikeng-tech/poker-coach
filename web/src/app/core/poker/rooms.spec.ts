import { SUPPORTED_ROOMS, roomName } from './rooms';

describe('roomName', () => {
  it('returns the brand name for known rooms', () => {
    expect(roomName('winamax')).toBe('Winamax');
    expect(roomName('betclic')).toBe('Betclic');
    expect(roomName('pmu')).toBe('PMU Poker');
    expect(roomName('pokerStars')).toBe('PokerStars');
  });

  it('returns the raw value for an unknown room', () => {
    expect(roomName('unknownRoom')).toBe('unknownRoom');
  });
});

describe('SUPPORTED_ROOMS', () => {
  it('contains exactly the rooms with shipped parsers', () => {
    expect(SUPPORTED_ROOMS).toEqual(['winamax', 'betclic', 'pmu']);
  });
});
