import { DependencyContainer } from "tsyringe";
import { Ilogger } from "@spt/models/spt/utils/Ilogger";
import { IPostDBLoadMod } from "@spt/models/external/IPostDBLoadMod";
import { DatabaseServer } from "@spt/servers/DatabaseServer";

import * as config from "../config.json"

class GigachadWorkout implements IPostDBLoadMod
{
	public postDBLoad(container: DependencyContainer): void 
	{
		const logger = container.resolve<Ilogger>("WinstonLogger");
		const db = container.resolve<DatabaseServer>("DatabaseServer");
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
				"multiplier": 6
			}];
			results.singleSuccessEffect.rewardsRange[1].levelMultipliers = [{
				"level": 0,
				"multiplier": 6
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

module.exports = { mod: new GigachadWorkout() }