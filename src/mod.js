"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || function (mod) {
    if (mod && mod.__esModule) return mod;
    var result = {};
    if (mod != null) for (var k in mod) if (k !== "default" && Object.prototype.hasOwnProperty.call(mod, k)) __createBinding(result, mod, k);
    __setModuleDefault(result, mod);
    return result;
};
Object.defineProperty(exports, "__esModule", { value: true });
const config = __importStar(require("../config.json"));
class GigachadWorkout {
    postDBLoad(container) {
        const logger = container.resolve("WinstonLogger");
        const db = container.resolve("DatabaseServer");
        const tables = db.getTables();
        const workoutCircles = tables.hideout.qte[0].quickTimeEvents;
        const firstCircle = workoutCircles[0];
        if (config.makeAllCirclesSameAsFirst) {
            for (let i = 1; i < workoutCircles.length; i++) {
                workoutCircles[i] = { ...firstCircle };
            }
        }
        workoutCircles.unshift(...Array.from({ length: config.extraCircles }, () => ({ ...firstCircle })));
        const results = tables.hideout.qte[0].results;
        results.finishEffect.rewardsRange[0].time = config.musclePainDuration;
        if (config.disableFracture) {
            delete results.singleFailEffect.rewardsRange;
        }
        if (config.sameExpRegardlessOfSkillLevel) {
            results.singleSuccessEffect.rewardsRange[0].levelMultipliers = [{
                    "level": 0,
                    "multiplier": 3
                }];
            results.singleSuccessEffect.rewardsRange[1].levelMultipliers = [{
                    "level": 0,
                    "multiplier": 3
                }];
        }
        const rewardsRange = results.singleSuccessEffect.rewardsRange;
        rewardsRange.forEach(reward => {
            reward.levelMultipliers.forEach(expByLevel => {
                expByLevel.multiplier *= config.expMultiplier;
            });
        });
    }
}
module.exports = { mod: new GigachadWorkout() };
//# sourceMappingURL=mod.js.map