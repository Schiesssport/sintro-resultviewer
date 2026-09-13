import { test, describe } from 'node:test';
import assert from 'node:assert/strict';

import {
    SECTOR_COUNT, sectorCentreAngle, pointOnCircle, ringWedgePath, shotDial, DEFAULT_DIAL_GEOMETRY,
} from '../core/sectors.js';

const geometry = { cx: 10, cy: 10, r: 9, innerR: 6 };

// The visual centre of gravity of a sector's wedge.
const midpoint = (sector) => pointOnCircle(sectorCentreAngle(sector), { ...geometry, r: geometry.r / 2 });
const filledSectors = (dial) => dial.wedges.filter((wedge) => wedge.filled).map((wedge) => wedge.sector);

describe('sector orientation', () => {
    // These are the directions measured from the device's own X/Y data.
    test('sector 1 is twelve o\'clock', () => assert.equal(sectorCentreAngle(1), 90));
    test('sector 3 is three o\'clock', () => assert.equal(sectorCentreAngle(3), 0));
    test('sector 5 is six o\'clock', () => assert.equal(sectorCentreAngle(5), -90));
    // -180 and 180 name the same direction; the quadrant test below pins the geometry.
    test('sector 7 is nine o\'clock', () => assert.equal(Math.abs(sectorCentreAngle(7)), 180));

    test('the numbers run clockwise, not counter-clockwise', () => {
        // Counter-clockwise numbering would put sector 2 upper-left and mirror every direction shown.
        const two = midpoint(2);
        assert.ok(two.x > geometry.cx, 'sector 2 is right of centre');
        assert.ok(two.y < geometry.cy, 'sector 2 is above centre');
    });

    test('every sector lands in the expected quadrant', () => {
        const above = (sector) => midpoint(sector).y < geometry.cy - 0.01;
        const below = (sector) => midpoint(sector).y > geometry.cy + 0.01;
        const right = (sector) => midpoint(sector).x > geometry.cx + 0.01;
        const left = (sector) => midpoint(sector).x < geometry.cx - 0.01;

        assert.ok(above(1) && !right(1) && !left(1), 'sector 1: top');
        assert.ok(above(2) && right(2), 'sector 2: top-right');
        assert.ok(right(3) && !above(3) && !below(3), 'sector 3: right');
        assert.ok(below(4) && right(4), 'sector 4: bottom-right');
        assert.ok(below(5) && !right(5) && !left(5), 'sector 5: bottom');
        assert.ok(below(6) && left(6), 'sector 6: bottom-left');
        assert.ok(left(7) && !above(7) && !below(7), 'sector 7: left');
        assert.ok(above(8) && left(8), 'sector 8: top-left');
    });

    test('the eight sectors are 45 degrees apart and cover the circle', () => {
        const angles = Array.from({ length: SECTOR_COUNT }, (_, i) => sectorCentreAngle(i + 1));
        const normalised = angles.map((a) => ((a % 360) + 360) % 360).sort((a, b) => a - b);

        assert.deepEqual(normalised, [0, 45, 90, 135, 180, 225, 270, 315]);
    });
});

describe('pointOnCircle', () => {
    test('90 degrees is up on screen, because SVG y grows downwards', () => {
        const point = pointOnCircle(90, geometry);
        assert.ok(Math.abs(point.x - 10) < 0.001);
        assert.ok(Math.abs(point.y - 1) < 0.001);
    });

    test('0 degrees is to the right', () => {
        const point = pointOnCircle(0, geometry);
        assert.ok(Math.abs(point.x - 19) < 0.001);
        assert.ok(Math.abs(point.y - 10) < 0.001);
    });
});

describe('shotDial', () => {
    test('fills exactly the reported sector', () => {
        assert.deepEqual(filledSectors(shotDial({ hitSector: 4 })), [4]);
    });

    test('a centre hit fills no wedge but is flagged', () => {
        // hitSector 0 means the shot is in the middle, not in any direction.
        const dial = shotDial({ hitSector: 0 });

        assert.equal(dial.isCentre, true);
        assert.deepEqual(filledSectors(dial), []);
    });

    test('no reported sector leaves the dial blank', () => {
        for (const shot of [{ hitSector: null }, {}, null]) {
            const dial = shotDial(shot);
            assert.equal(dial.isCentre, false);
            assert.deepEqual(filledSectors(dial), []);
        }
    });

    test('a miss still gets a dial, because the direction is the point', () => {
        assert.deepEqual(filledSectors(shotDial({ value: 0, hitSector: 6 })), [6]);
    });

    test('always describes all eight wedges', () => {
        assert.equal(shotDial({ hitSector: 3 }).wedges.length, SECTOR_COUNT);
    });
});

describe('ringWedgePath', () => {
    test('leaves the middle open so the ring value stays readable', () => {
        const path = ringWedgePath(1, geometry);

        // Two arcs and no line to the centre: a pie wedge would start "M 10 10".
        assert.ok(!path.startsWith('M 10 10'), 'must not start at the centre');
        assert.equal((path.match(/A /g) ?? []).length, 2, 'outer and inner arc');
        assert.match(path, /Z$/);
    });

    test('honours the inner radius', () => {
        const thin = ringWedgePath(1, { ...geometry, innerR: 8 });
        const thick = ringWedgePath(1, { ...geometry, innerR: 2 });
        assert.notEqual(thin, thick);
    });

    test('every sector produces a distinct ring wedge', () => {
        const paths = Array.from({ length: SECTOR_COUNT }, (_, i) => ringWedgePath(i + 1, geometry));
        assert.equal(new Set(paths).size, SECTOR_COUNT);
    });
});

describe('shotDial ring paths', () => {
    test('each wedge carries a ring path', () => {
        for (const wedge of shotDial({ hitSector: 2 }).wedges) {
            assert.ok(wedge.ringPath.length > 0);
        }
    });
});

describe('default ring geometry', () => {
    test('the dial is a circle, not an ellipse', () => {
        // It depicts a target face; the size is chosen for the widest value instead of stretching.
        const { cx, cy } = DEFAULT_DIAL_GEOMETRY;
        assert.equal(cx, cy, 'the viewBox must be square so the SVG renders a circle');
    });

    test('the hole is wide enough for a three-digit score', () => {
        // The 100er valuation scores 0-100, so "100" has to fit inside the ring.
        const { r, innerR } = DEFAULT_DIAL_GEOMETRY;
        const holeFraction = innerR / r;

        assert.ok(holeFraction > 0.65, `hole is only ${Math.round(holeFraction * 100)}% of the dial`);
        assert.ok(holeFraction < 0.85, 'the ring must still be visible');
    });

    test('the ring is thin but not hairline', () => {
        const { r, innerR } = DEFAULT_DIAL_GEOMETRY;
        assert.ok(r - innerR >= 2, 'ring thickness must survive scaling down');
    });
});
